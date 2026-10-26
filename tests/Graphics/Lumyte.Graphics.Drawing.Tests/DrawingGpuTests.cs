using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Checks uploaded indexed model geometry and fullscreen effects through Vulkan pixel readback.</summary>
public sealed class DrawingGpuTests
{
    /// <summary>Checks unlit material color, HDR sampling, exposure and grayscale output.</summary>
    /// <returns>The asynchronous rendering check.</returns>
    [DrawingGpuFact]
    public async Task UnlitModelAndEffectsRenderAsync() => await RenderAsync(lit: false, expected: 190);

    /// <summary>Checks normal-mapped PBR execution, emissive color, fog and fullscreen effects.</summary>
    /// <returns>The asynchronous rendering check.</returns>
    [DrawingGpuFact]
    public async Task LitModelFogAndEffectsRenderAsync() => await RenderAsync(lit: true, expected: 95);

    /// <summary>Checks the base-color UV transform samples the selected texel rather than the original coordinates.</summary>
    /// <returns>The asynchronous rendering check.</returns>
    [DrawingGpuFact]
    public async Task MaterialTextureTransformRendersAsync() => await RenderAsync(lit: false, expected: 37, transformed: true);

    /// <summary>Checks HDR emissive strength multiplies the emitted material color.</summary>
    /// <returns>The asynchronous rendering check.</returns>
    [DrawingGpuFact]
    public async Task EmissiveStrengthRendersAsync() => await RenderAsync(lit: true, expected: 237, strongEmission: true);

    /// <summary>Checks straight-color grading returns premultiplied RGB and preserves scene alpha.</summary>
    /// <returns>The asynchronous rendering check.</returns>
    [DrawingGpuFact]
    public async Task TransparentModelEffectsPreserveAlphaAsync() => await RenderAsync(lit: false, expected: 95, transparent: true);

    /// <summary>Checks tone mapping changes values and explicitly marks display-linear output.</summary>
    /// <returns>The asynchronous rendering check.</returns>
    [DrawingGpuFact]
    public async Task SceneToneMappingRendersAsync() => await RenderAsync(lit: false, expected: 109, toneMap: true);

    /// <summary>Checks the vignette attenuates corners while preserving the center.</summary>
    /// <returns>The asynchronous rendering check.</returns>
    [DrawingGpuFact]
    public async Task VignetteRendersAsync() => await RenderAsync(lit: false, expected: 190, vignette: 1);

    /// <summary>Checks smooth dielectric highlights against the analytic GGX peak under direct lighting.</summary>
    /// <returns>The asynchronous rendering checks.</returns>
    [DrawingGpuFact]
    public async Task SmoothMaterialsRetainGgxHighlightsAsync()
    {
        foreach (float roughness in new[] { 0.045f, 0.1f, 0.2f })
        {
            float expected = (0.96f / MathF.PI) + (0.01f / (MathF.PI * MathF.Pow(roughness, 4)));
            await RenderAsync(lit: true, expected: (int)MathF.Round(expected * 510), litRoughness: roughness);
        }
    }

    /// <summary>Checks rotating a constant normal map remains stable across uniform UV scales.</summary>
    /// <returns>The asynchronous rendering checks.</returns>
    [DrawingGpuFact]
    public async Task NormalUvRotationIsIndependentOfDerivativeMagnitudeAsync()
    {
        foreach (float scale in new[] { 0.004f, 0.002f, 0.001f })
        {
            await RenderAsync(lit: true, expected: 0, normalUvScale: scale);
        }
    }

    private static void Transition(IGraphicsCommandBuffer commands, IGraphicsTexture texture, TextureState beforeState, TextureState afterState, BarrierScope before, BarrierScope after) => commands.Barrier(new TextureBarrierDesc
    {
        Texture = texture,
        Range = new(0, 1, 0, 1),
        BeforeState = beforeState,
        AfterState = afterState,
        Before = before,
        After = after,
    });

    private static void Setup(IRenderEncoder render, IGraphicsPipeline pipeline, GraphicsRenderStateDesc state)
    {
        render.SetPipeline(pipeline);
        render.SetRenderState(state);
        render.SetViewport(new(0, 0, 8, 8, 0, 1));
        render.SetScissor(new(0, 0, 8, 8));
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(0);
    }

    private static async Task RenderAsync(bool lit, int expected, bool transformed = false, bool strongEmission = false, bool transparent = false, bool toneMap = false, float vignette = 0, float? litRoughness = null, float? normalUvScale = null)
    {
        using var device = VulkanDevice.Create();
        ShaderArtifact vertexArtifact = DrawingShaders.LoadModelVertex();
        ModelVertex[] vertices =
        [
            new(new(-1, -1, 0.5f), Vector3.UnitZ, new(1, 0, 0, 1), Vector2.Zero, Vector4.One),
            new(new(3, -1, 0.5f), Vector3.UnitZ, new(1, 0, 0, 1), Vector2.UnitX, Vector4.One),
            new(new(-1, 3, 0.5f), Vector3.UnitZ, new(1, 0, 0, 1), Vector2.UnitY, Vector4.One),
        ];
        IDisposable? creationCleanup = null;
        using ModelMesh mesh = lit
            ? await ModelMesh.CreatePackedAsync(device, MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray(), new ushort[] { 0, 1, 2 }, vertexArtifact, owner => creationCleanup = owner)
            : await ModelMesh.CreateAsync(device, vertices, new uint[] { 0, 1, 2 }, vertexArtifact);
        creationCleanup?.Dispose();
        Assert.Equal(lit ? IndexFormat.Uint16 : IndexFormat.Uint32, mesh.IndexFormat);
        Assert.Equal(3u, mesh.IndexCount);
        using (IGraphicsCommandBuffer initial = device.CreateCommandBuffer(new()))
        {
            mesh.RecordInitialUpload(initial);
            initial.Finish();
            using IGraphicsSubmission uploaded = device.Queue.Submit([initial]);
            await uploaded.WaitAsync();
        }

        mesh.ReleaseUploadResources();
        mesh.ReleaseUploadResources();
        using (IGraphicsCommandBuffer unused = device.CreateCommandBuffer(new()))
        {
            Assert.Throws<InvalidOperationException>(() => mesh.RecordInitialUpload(unused));
        }

        using IGraphicsShader vertex = device.CreateShader(vertexArtifact);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders.LoadModelFragment());
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using IGraphicsTexture scene = device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled });
        using IGraphicsTexture depth = device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Depth32Float, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTexture white = device.CreateTexture(new() { Width = 2, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination | TextureUsage.Sampled });
        using IGraphicsTexture normal = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination | TextureUsage.Sampled });
        using IGraphicsTextureView whiteView = white.CreateView();
        using IGraphicsTextureView normalView = normal.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new() { AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 });
        using IGraphicsBuffer<byte> upload = device.CreateBuffer<byte>(new() { Count = 12, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        await upload.MapAsync();
        upload.CopyFrom(transformed
            ? new byte[] { 255, 0, 0, 255, 0, 0, 255, 255, 128, 128, 255, 255 }
            : new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 128, 128, 255, 255 });
        if (normalUvScale.HasValue)
        {
            upload.Slice(8, 4).CopyFrom(new byte[] { 255, 128, 255, 255 });
        }

        upload.Unmap();
        TextureCopyLayout copyLayout = device.GetTextureCopyLayout(TextureFormat.Rgba16Float);
        uint pitch = ((64 + copyLayout.BytesPerRowAlignment - 1) / copyLayout.BytesPerRowAlignment) * copyLayout.BytesPerRowAlignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1, TextureCapacity = 3, SamplerCapacity = 1 });
        IGpuRef<ModelVertex> vertexRef = table.WriteBuffer(0, mesh.Vertices.Slice(0, mesh.Vertices.Count));
        object vertexResource = Assert.IsAssignableFrom<IShaderReference>(vertexRef).Resource;
        Assert.IsAssignableFrom<IShaderRawBuffer>(vertexResource);
        Assert.False(vertexResource is IShaderDataSource);
        IGpuRef<IGraphicsSampler> samplerRef = table.WriteSampler(0, sampler);
        var whiteRef = new SampledTexture(table.WriteTexture(0, whiteView), samplerRef);
        var normalRef = new SampledTexture(table.WriteTexture(1, normalView), samplerRef);
        ModelMaterial factors = ModelDrawing.DefaultMaterial with { BaseColor = new(0.2f, 0.4f, 0.6f, 1), Emissive = new(0.2f, 0.4f, 0.6f), Unlit = lit ? 0u : 1u };
        ModelMaterial material = transformed
            ? factors with { BaseColor = Vector4.One, BaseColorUv = new(new(0.75f, 0.5f), Vector2.Zero, 0) }
            : factors;
        if (strongEmission)
        {
            material = material with { EmissiveStrength = 1.25f };
        }

        if (transparent)
        {
            material = material with { BaseColor = material.BaseColor with { W = 0.5f }, AlphaMode = 2 };
        }

        if (litRoughness.HasValue || normalUvScale.HasValue)
        {
            material = material with
            {
                BaseColor = Vector4.One,
                Emissive = Vector3.Zero,
                Metallic = 0,
                Roughness = litRoughness ?? 0.5f,
                NormalScale = normalUvScale.HasValue ? 1 : 0,
                NormalUv = normalUvScale is { } scale ? new(Vector2.Zero, new(scale), MathF.PI) : ModelTextureTransform.Identity,
            };
        }

        ModelDrawArguments arguments = ModelDrawing.CreateArguments(vertexRef, material, whiteRef, normalRef) with
        {
            CameraPosition = new(0, 0, 10),
            LightColor = Vector3.Zero,
            AmbientColor = Vector3.Zero,
            FogColor = new(0.1f, 0.2f, 0.3f),
            FogDensity = lit && !strongEmission ? 1 : 0,
        };
        if (litRoughness.HasValue || normalUvScale.HasValue)
        {
            arguments = arguments with
            {
                CameraPosition = new(0, 0, 10000),
                LightColor = Vector3.One,
                LightDirection = normalUvScale.HasValue ? Vector3.Normalize(new Vector3(1, 0, 1)) : Vector3.UnitZ,
                FogDensity = 0,
            };
        }

        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        var sampleRead = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        var colorWrite = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = upload.Slice(0, 12), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = copyRead });
        Transition(commands, white, TextureState.Undefined, TextureState.CopyDestination, new(PipelineStage.None, ResourceAccess.None), copyWrite);
        Transition(commands, normal, TextureState.Undefined, TextureState.CopyDestination, new(PipelineStage.None, ResourceAccess.None), copyWrite);
        commands.CopyBufferToTexture(new() { Buffer = upload.Slice(0, 8), BytesPerRow = 8, RowsPerImage = 1 }, new() { Texture = white, Width = 2, Height = 1 });
        commands.CopyBufferToTexture(new() { Buffer = upload.Slice(8, 4), BytesPerRow = 4, RowsPerImage = 1 }, new() { Texture = normal, Width = 1, Height = 1 });
        Transition(commands, white, TextureState.CopyDestination, TextureState.Sampled, copyWrite, sampleRead);
        Transition(commands, normal, TextureState.CopyDestination, TextureState.Sampled, copyWrite, sampleRead);
        using var graph = new Graph(device);
        ModelMeshGraphResources meshResources = mesh.Import(
            graph,
            new(PipelineStage.VertexShader, ResourceAccess.ShaderRead),
            new(PipelineStage.IndexInput, ResourceAccess.IndexRead));
        RenderGraphTexture sceneHandle = graph.ImportTexture(scene, TextureState.Undefined, default);
        RenderGraphTexture depthHandle = graph.ImportTexture(depth, TextureState.Undefined, default);
        RenderGraphTexture whiteHandle = graph.ImportTexture(white, TextureState.Sampled, sampleRead);
        RenderGraphTexture normalHandle = graph.ImportTexture(normal, TextureState.Sampled, sampleRead);
        RenderGraphPass modelPass = graph.AddPass("model", context =>
        {
            IRenderEncoder render = context.Commands.BeginRenderPass(new()
            {
                ColorAttachments = [new() { View = context.CreateTextureView(sceneHandle), LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }],
                DepthStencilAttachment = new() { View = context.CreateTextureView(depthHandle) },
            });
            Setup(render, pipeline, ModelDrawing.CreateRenderState(material, doubleSided: true));
            render.SetArgumentTable(table);
            render.SetArguments(in arguments);
            mesh.Draw(render);
            render.End();
        }).UseTexture(sceneHandle, TextureState.ColorAttachment, colorWrite)
            .UseTexture(depthHandle, TextureState.DepthStencilAttachment, new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite))
            .UseTexture(whiteHandle, TextureState.Sampled, sampleRead)
            .UseTexture(normalHandle, TextureState.Sampled, sampleRead);
        meshResources.Use(modelPass);
        var source = new EffectImage(sceneHandle, new(0, 0, 8, 8), EffectColorSemantic.SceneLinear);
        EffectImage graded = toneMap
            ? graph.AppendEffects(ImageEffectGraph.Create(new DelegatingOpacity(), ImageEffectGraph.Create(new PostProcessEffect(1, 0, vignette, toneMap: true), ImageEffectGraph.Source())), source)
            : graph.AppendPostProcess(source, exposure: 1, saturation: 0, vignette: vignette);
        Assert.Equal(toneMap ? EffectColorSemantic.DisplayLinear : EffectColorSemantic.SceneLinear, graded.ColorSemantic);
        RenderGraphBuffer<byte> readbackHandle = graph.ImportBuffer(readback, default);
        graph.AddPass("readback", context => context.Commands.CopyTextureToBuffer(
            new() { Texture = context.GetTexture(graded.Texture), Width = 8, Height = 8 },
            new() { Buffer = context.GetBuffer(readbackHandle).Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 8 }))
            .UseTexture(graded.Texture, TextureState.CopySource, copyRead)
            .UseBuffer(readbackHandle, copyWrite);
        graph.ExportBuffer(readbackHandle, new(PipelineStage.Host, ResourceAccess.HostRead));
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(toneMap ? new[] { "model", "image-effect", "image-effect", "readback" } : new[] { "model", "image-effect", "readback" }, plan.PassNames);
        plan.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] bytes = new byte[checked((int)readback.Count)];
        readback.CopyTo(bytes);
        readback.Unmap();
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int offset = checked((int)(y * pitch) + (x * 8));
                float radius = new Vector2(((x + 0.5f) / 8) - 0.5f, ((y + 0.5f) / 8) - 0.5f).Length();
                float weight = Math.Clamp((radius - 0.2f) / 0.5f, 0, 1);
                float expectedPixel = expected * (1 - (vignette * weight * weight * (3 - (2 * weight))));
                for (int channel = 0; channel < 3; channel++)
                {
                    float tolerance = litRoughness.HasValue ? expectedPixel * 0.003f : 2;
                    Assert.InRange((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, offset + (channel * 2))) * 255, expectedPixel - tolerance, expectedPixel + tolerance);
                }

                Assert.Equal(transparent ? (Half)0.5f : (Half)1, BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, offset + 6)));
            }
        }
    }

    private sealed record DelegatingOpacity : ImageEffectDefinition
    {
        public override int InputCount => 1;

        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;

        public override EffectImage AppendPasses(EffectPreparationContext context, Graph plan, ReadOnlySpan<EffectImage> inputs)
            => plan.AppendEffectWithContext(new OpacityEffect(1), context, inputs.ToArray());
    }
}
