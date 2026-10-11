using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Xunit;

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

    private static async Task RenderAsync(bool lit, int expected, bool transformed = false, bool strongEmission = false)
    {
        using var device = VulkanDevice.Create();
        ShaderArtifact vertexArtifact = DrawingShaders.LoadModelVertex();
        ModelVertex[] vertices =
        [
            new(new(-1, -1, 0.5f), Vector3.UnitZ, new(1, 0, 0, 1), Vector2.Zero, Vector4.One),
            new(new(3, -1, 0.5f), Vector3.UnitZ, new(1, 0, 0, 1), Vector2.UnitX, Vector4.One),
            new(new(-1, 3, 0.5f), Vector3.UnitZ, new(1, 0, 0, 1), Vector2.UnitY, Vector4.One),
        ];
        using ModelMesh mesh = lit
            ? await ModelMesh.CreatePackedAsync(device, MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray(), new ushort[] { 0, 1, 2 }, vertexArtifact)
            : await ModelMesh.CreateAsync(device, vertices, new uint[] { 0, 1, 2 }, vertexArtifact);
        Assert.Equal(lit ? IndexFormat.Uint16 : IndexFormat.Uint32, mesh.IndexFormat);
        Assert.Equal(3u, mesh.IndexCount);
        using IGraphicsShader vertex = device.CreateShader(vertexArtifact);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders.LoadModelFragment());
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using IGraphicsShader effectVertex = device.CreateShader(DrawingShaders.LoadPostProcessVertex());
        using IGraphicsShader effectFragment = device.CreateShader(DrawingShaders.LoadPostProcessFragment());
        using IGraphicsPipeline effectPipeline = device.CreateGraphicsPipeline(new() { VertexShader = effectVertex, FragmentShader = effectFragment });
        using IGraphicsTexture scene = device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled });
        using IGraphicsTexture output = device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTexture depth = device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Depth32Float, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTexture white = device.CreateTexture(new() { Width = 2, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination | TextureUsage.Sampled });
        using IGraphicsTexture normal = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination | TextureUsage.Sampled });
        using IGraphicsTextureView sceneView = scene.CreateView();
        using IGraphicsTextureView outputView = output.CreateView();
        using IGraphicsTextureView depthView = depth.CreateView();
        using IGraphicsTextureView whiteView = white.CreateView();
        using IGraphicsTextureView normalView = normal.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new() { AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 });
        using IGraphicsBuffer<byte> upload = device.CreateBuffer<byte>(new() { Count = 12, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        await upload.MapAsync();
        upload.CopyFrom(transformed
            ? new byte[] { 255, 0, 0, 255, 0, 0, 255, 255, 128, 128, 255, 255 }
            : new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 128, 128, 255, 255 });
        upload.Unmap();
        TextureCopyLayout copyLayout = device.GetTextureCopyLayout(TextureFormat.Rgba8Unorm);
        uint pitch = ((32 + copyLayout.BytesPerRowAlignment - 1) / copyLayout.BytesPerRowAlignment) * copyLayout.BytesPerRowAlignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1, TextureCapacity = 3, SamplerCapacity = 1 });
        IGpuRef<ModelVertex> vertexRef = table.WriteBuffer(0, mesh.Vertices.Slice(0, mesh.Vertices.Count));
        object vertexResource = Assert.IsAssignableFrom<IShaderReference>(vertexRef).Resource;
        Assert.IsAssignableFrom<IShaderRawBuffer>(vertexResource);
        Assert.False(vertexResource is IShaderDataSource);
        IGpuRef<IGraphicsSampler> samplerRef = table.WriteSampler(0, sampler);
        var whiteRef = new SampledTexture(table.WriteTexture(0, whiteView), samplerRef);
        var normalRef = new SampledTexture(table.WriteTexture(1, normalView), samplerRef);
        IGpuRef<IGraphicsTextureView> sceneRef = table.WriteTexture(2, sceneView);
        ModelMaterial factors = ModelDrawing.DefaultMaterial with { BaseColor = new(0.2f, 0.4f, 0.6f, 1), Emissive = new(0.2f, 0.4f, 0.6f), Unlit = lit ? 0u : 1u };
        ModelMaterial material = transformed
            ? factors with { BaseColor = Vector4.One, BaseColorUv = new(new(0.75f, 0.5f), Vector2.Zero, 0) }
            : factors;
        if (strongEmission)
        {
            material = material with { EmissiveStrength = 1.25f };
        }

        ModelDrawArguments arguments = ModelDrawing.CreateArguments(vertexRef, material, whiteRef, normalRef) with
        {
            CameraPosition = new(0, 0, 10),
            LightColor = Vector3.Zero,
            AmbientColor = Vector3.Zero,
            FogColor = new(0.1f, 0.2f, 0.3f),
            FogDensity = lit && !strongEmission ? 1 : 0,
        };
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        var sampleRead = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        var colorWrite = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        mesh.RecordInitialUpload(commands);
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = upload.Slice(0, 12), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = copyRead });
        Transition(commands, white, TextureState.Undefined, TextureState.CopyDestination, new(PipelineStage.None, ResourceAccess.None), copyWrite);
        Transition(commands, normal, TextureState.Undefined, TextureState.CopyDestination, new(PipelineStage.None, ResourceAccess.None), copyWrite);
        commands.CopyBufferToTexture(new() { Buffer = upload.Slice(0, 8), BytesPerRow = 8, RowsPerImage = 1 }, new() { Texture = white, Width = 2, Height = 1 });
        commands.CopyBufferToTexture(new() { Buffer = upload.Slice(8, 4), BytesPerRow = 4, RowsPerImage = 1 }, new() { Texture = normal, Width = 1, Height = 1 });
        Transition(commands, white, TextureState.CopyDestination, TextureState.Sampled, copyWrite, sampleRead);
        Transition(commands, normal, TextureState.CopyDestination, TextureState.Sampled, copyWrite, sampleRead);
        Transition(commands, scene, TextureState.Undefined, TextureState.ColorAttachment, new(PipelineStage.None, ResourceAccess.None), colorWrite);
        Transition(commands, depth, TextureState.Undefined, TextureState.DepthStencilAttachment, new(PipelineStage.None, ResourceAccess.None), new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite));
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = sceneView, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }], DepthStencilAttachment = new() { View = depthView } });
        Setup(render, pipeline, ModelDrawing.CreateRenderState(material, doubleSided: true));
        render.SetArguments(in arguments);
        mesh.Draw(render);
        render.End();
        Transition(commands, scene, TextureState.ColorAttachment, TextureState.Sampled, colorWrite, sampleRead);
        Transition(commands, output, TextureState.Undefined, TextureState.ColorAttachment, new(PipelineStage.None, ResourceAccess.None), colorWrite);
        render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = outputView, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        Setup(render, effectPipeline, new() { ColorTargets = [new()] });
        var effects = new PostProcessArguments(sceneRef, samplerRef, 1, 0, 0, 1, 0);
        render.SetArguments(in effects);
        render.Draw(3);
        render.End();
        Transition(commands, output, TextureState.ColorAttachment, TextureState.CopySource, colorWrite, copyRead);
        commands.CopyTextureToBuffer(new() { Texture = output, Width = 8, Height = 8 }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 8 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
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
                int offset = checked((int)(y * pitch) + (x * 4));
                for (int channel = 0; channel < 3; channel++)
                {
                    Assert.InRange((int)bytes[offset + channel], expected - 2, expected + 2);
                }

                Assert.Equal(255, bytes[offset + 3]);
            }
        }
    }
}
