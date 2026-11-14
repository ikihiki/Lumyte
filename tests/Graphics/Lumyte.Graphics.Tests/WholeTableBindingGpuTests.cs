using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks native binding includes written textures not reachable through typed root references.</summary>
public sealed class WholeTableBindingGpuTests
{
    private const string VertexSource = """
        float4 main(uint index : SV_VertexID) : SV_Position
        {
            float2 p = index == 0 ? float2(-1, -1) : (index == 1 ? float2(3, -1) : float2(-1, 3));
            return float4(p, 0, 1);
        }
        """;

    private const string FragmentSource = """
        #include "lumyte.slang"
        struct WholeTableRoot { uint TextureIndex; GpuSamplerRef Sampler; };
        ConstantBuffer<WholeTableRoot> arguments;
        float4 main() : SV_Target0
        {
            GpuTextureRef texture;
            texture.Id = arguments.TextureIndex;
            texture.Offset = 0;
            texture.Count = 1;
            texture.Reserved = 0;
            return LumyteSampleGrad(texture, arguments.Sampler, float2(0.5, 0.5), float2(0, 0), float2(0, 0));
        }
        """;

    private const string LiteralFragmentSource = """
        #include "lumyte.slang"
        float4 main() : SV_Target0
        {
            GpuTextureRef texture;
            texture.Id = 3;
            texture.Offset = 0;
            texture.Count = 1;
            texture.Reserved = 0;
            GpuSamplerRef sampler;
            sampler.Id = 0;
            sampler.Offset = 0;
            sampler.Count = 1;
            sampler.Reserved = 0;
            return LumyteSampleGrad(texture, sampler, float2(0.5, 0.5), float2(0, 0), float2(0, 0));
        }
        """;

    /// <summary>Vulkan binds sparse written slots with validation both disabled and enabled.</summary>
    /// <returns>The completed native readback checks.</returns>
    [GpuFact]
    public async Task VulkanWholeTableNumericTextureIndexRendersGreenAsync()
    {
        foreach (bool validation in new[] { false, true })
        {
            using var device = VulkanDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await RenderAsync(device, ShaderTarget.SpirV, literal: false);
            await RenderAsync(device, ShaderTarget.SpirV, literal: true);
        }
    }

    /// <summary>Wgpu projects sparse written slots without confusing capacity with registrations.</summary>
    /// <returns>The completed native readback checks.</returns>
    [GpuFact]
    public async Task WgpuWholeTableNumericTextureIndexRendersGreenAsync()
    {
        foreach (bool validation in new[] { false, true })
        {
            using var device = WgpuDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await RenderAsync(device, ShaderTarget.Wgsl, literal: false);
            await RenderAsync(device, ShaderTarget.Wgsl, literal: true);
        }
    }

    private static async Task RenderAsync(IGraphicDevice device, ShaderTarget target, bool literal)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var compiler = new SlangShaderCompiler();
        ShaderArtifact vertex = await compiler.CompileAsync(new() { Source = VertexSource, Stage = ShaderStage.Vertex, Target = target }, timeout.Token);
        ShaderArtifact fragment = await compiler.CompileAsync(new() { Source = literal ? LiteralFragmentSource : FragmentSource, Stage = ShaderStage.Fragment, Target = target }, timeout.Token);
        using IGraphicsShader vertexShader = device.CreateShader(vertex);
        using IGraphicsShader fragmentShader = device.CreateShader(fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertexShader, FragmentShader = fragmentShader });
        using IGraphicsTexture red = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled });
        using IGraphicsTexture green = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled });
        using IGraphicsTextureView redView = red.CreateView();
        using IGraphicsTextureView greenView = green.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new());
        using IArgumentTable table = device.CreateArgumentTable(new() { TextureCapacity = 16, SamplerCapacity = 16 });
        IGpuRef<IGraphicsTextureView> a = table.WriteTexture(1, redView);
        IGpuRef<IGraphicsTextureView> b = table.WriteTexture(3, greenView);
        IGpuRef<IGraphicsSampler> samplerRef = table.WriteSampler(0, sampler);
        var root = new WholeTableRoot(3, samplerRef);

        // A safe managed negative control: the former reachable-only projection cannot bind B.
        // Never submit that incomplete descriptor set to a GPU.
        IShaderReference[] reachable = literal ? [] : [(IShaderReference)samplerRef];
        var metadata = ShaderRootArguments.Create("arguments", "WholeTableRoot", literal ? 0 : WholeTableRoot.TransferSize, new ShaderDependencyMetadata(reachable));
        var dependencies = ShaderBindingDependencies.Collect(metadata, table);
        Assert.Equal(reachable.Length, dependencies.References.Count);
        Assert.DoesNotContain(dependencies.References, reference => ReferenceEquals(reference.Resource, greenView));
        var projectionTarget = new ShaderTargetData(ShaderTarget.Wgsl, ShaderStage.Fragment, "main", "test", "row-major", [], "{}");
        var oldProjection = new ShaderBindingData(dependencies.References, projectionTarget, device.Caps);
        Assert.Empty(oldProjection.Textures);
        var completeProjection = new ShaderBindingData([(IShaderReference)a, (IShaderReference)b, (IShaderReference)samplerRef], projectionTarget, device.Caps);
        Assert.Equal(2, completeProjection.Textures.Count);
        Assert.Single(completeProjection.Samplers);
        Assert.Equal(4 * 16, completeProjection.Map.Length);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(completeProjection.Map.AsSpan(3 * 16)));

        using IGraphicsTexture output = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView outputView = output.CreateView();
        TextureCopyLayout copyLayout = device.GetTextureCopyLayout(output.Format);
        uint pitch = checked(((copyLayout.BytesPerTexel + copyLayout.BytesPerRowAlignment - 1) / copyLayout.BytesPerRowAlignment) * copyLayout.BytesPerRowAlignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        ClearSampledTexture(red, redView, new(1, 0, 0, 1));
        ClearSampledTexture(green, greenView, new(0, 1, 0, 1));
        commands.Barrier(new TextureBarrierDesc { Texture = output, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = outputView, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0, 0, 1, 1) }] });
        bool prepared = false;
        bool admitted = false;
        try
        {
            render.SetPipeline(pipeline);
            render.SetArgumentTable(table);
            render.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
            render.SetViewport(new(0, 0, 1, 1, 0, 1));
            render.SetScissor(new(0, 0, 1, 1));
            render.SetBlendConstant(new(0, 0, 0, 0));
            render.SetStencilReference(0);
            if (!literal)
            {
                if (device.ValidationEnabled)
                {
                    // Observe actual native root metadata, not a snapshot or a test-side reconstruction.
                    render.SetArgumentValidation(references =>
                    {
                        Assert.Same(sampler, Assert.Single(references.References).Resource);
                        prepared = true;
                        return referencesAtDraw =>
                        {
                            Assert.Same(sampler, Assert.Single(referencesAtDraw).Resource);
                            admitted = true;
                        };
                    });
                }

                render.SetArguments(in root);
            }

            render.Draw(3);
        }
        finally
        {
            render.End();
        }

        if (device.ValidationEnabled && !literal)
        {
            Assert.True(prepared);
            Assert.True(admitted);
        }

        commands.Barrier(new TextureBarrierDesc { Texture = output, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyTextureToBuffer(new() { Texture = output, Width = 1, Height = 1 }, new() { Buffer = readback.SliceElements(0, pitch), BytesPerRow = pitch, RowsPerImage = 1 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, pitch), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync(timeout.Token);
        await readback.MapAsync(timeout.Token);
        byte[] pixels = new byte[pitch];
        readback.CopyTo(pixels);
        readback.Unmap();
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels[..4]);

        void ClearSampledTexture(IGraphicsTexture texture, IGraphicsTextureView view, ClearColor color)
        {
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
            IRenderEncoder clear = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = color }] });
            clear.End();
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.Sampled, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        }
    }

    // A single sampler fixup follows a raw numeric index; no texture IGpuRef enters the root.
    private readonly struct WholeTableRoot(uint textureIndex, IGpuRef<IGraphicsSampler> sampler) : IShaderArguments
    {
        public static string ShaderTypeName => "WholeTableRoot";

        public static int TransferSize => 20;

        public static int ReferenceCount => 1;

        public static IReadOnlyList<ShaderTransferField> TransferFields { get; } =
        [
            new("TextureIndex", 0, 4, typeof(uint)),
            new("Sampler", 4, 16, typeof(ShaderReferenceSlot), true),
        ];

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            uint index = textureIndex;
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in index, 1)));
            writer.WriteReference(sampler);
        }
    }
}
