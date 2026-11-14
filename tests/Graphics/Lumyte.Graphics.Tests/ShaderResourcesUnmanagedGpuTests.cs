using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks unmanaged resource helpers reach native root storage and produce shader-visible pixels.</summary>
public sealed class ShaderResourcesUnmanagedGpuTests
{
    private const string VertexSource = """
        float4 main(uint index : SV_VertexID) : SV_Position
        {
            float2 position = index == 0 ? float2(-1, -1) : (index == 1 ? float2(3, -1) : float2(-1, 3));
            return float4(position, 0, 1);
        }
        """;

    private const string FragmentSource = """
        struct HelperColorRoot { float Red; float Green; float Blue; float Alpha; uint Gain; };
        ConstantBuffer<HelperColorRoot> arguments;
        float4 main() : SV_Target { return float4(arguments.Red, arguments.Green, arguments.Blue, arguments.Alpha) * float(arguments.Gain); }
        """;

    private const string UniformFragmentSource = """
        struct HelperColorRoot { float4 Color; };
        ConstantBuffer<HelperColorRoot> arguments;
        float4 main() : SV_Target { return arguments.Color; }
        """;

    /// <summary>Vulkan renders an ordinary unmanaged root with diagnostics disabled and enabled.</summary>
    /// <returns>The completed pixel checks.</returns>
    [GpuFact]
    public async Task VulkanUnmanagedHelperRendersNativeRootAsync()
    {
        foreach (bool validation in new[] { false, true })
        {
            using var device = VulkanDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await RenderAsync(device, ShaderTarget.SpirV);
        }
    }

    /// <summary>Wgpu renders an ordinary unmanaged root with diagnostics disabled and enabled.</summary>
    /// <returns>The completed pixel checks.</returns>
    [GpuFact]
    public async Task WgpuUnmanagedHelperRendersNativeRootAsync()
    {
        foreach (bool validation in new[] { false, true })
        {
            using var device = WgpuDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await RenderAsync(device, ShaderTarget.Wgsl);
        }
    }

    private static async Task RenderAsync(IGraphicDevice device, ShaderTarget target)
    {
        Assert.False(typeof(IShaderArguments).IsAssignableFrom(typeof(HelperColorRoot)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var compiler = new SlangShaderCompiler();
        ShaderArtifact vertex = await compiler.CompileAsync(new() { Source = VertexSource, Stage = ShaderStage.Vertex, Target = target }, timeout.Token);
        ShaderArtifact fragment = await compiler.CompileAsync(new() { Source = target == ShaderTarget.SpirV ? FragmentSource : UniformFragmentSource, Stage = ShaderStage.Fragment, Target = target }, timeout.Token);
        using IGraphicsShader vertexShader = device.CreateShader(vertex);
        using IGraphicsShader fragmentShader = device.CreateShader(fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertexShader, FragmentShader = fragmentShader });
        using IGraphicsTexture texture = device.CreateTexture(new()
        {
            Width = 2,
            Height = 1,
            Format = TextureFormat.Rgba8Unorm,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        });
        using IGraphicsTextureView view = texture.CreateView();
        TextureCopyLayout layout = device.GetTextureCopyLayout(texture.Format);
        uint pitch = checked((((2 * layout.BytesPerTexel) + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using var plan = new ArgumentTablePlan(device);
        ShaderResources resources = plan.CreateShaderResources();
        plan.ConfigureExecution(resources, 1);
        plan.MaterializeResources([resources], timeout.Token);
        Assert.Empty(plan.Tables);
        IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        bool commandsCompleted = false;
        try
        {
            commands.Barrier(new TextureBarrierDesc
            {
                Texture = texture,
                Range = new(0, 1, 0, 1),
                BeforeState = TextureState.Undefined,
                AfterState = TextureState.ColorAttachment,
                Before = default,
                After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite),
            });
            var encoder = new StrictNativeEncoder(commands.BeginRenderPass(new()
            {
                ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0, 0, 0, 0) }],
            }));
            bool renderBodyCompleted = false;
            try
            {
                encoder.SetPipeline(pipeline);
                encoder.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
                encoder.SetViewport(new(0, 0, 2, 1, 0, 1));
                encoder.SetScissor(new(0, 0, 1, 1));
                encoder.SetBlendConstant(new(0, 0, 0, 0));
                encoder.SetStencilReference(0);
                DrawColor(new(1, 0, 0, 1));
                encoder.SetScissor(new(1, 0, 1, 1));
                DrawColor(new(0, 1, 0, 1));
                Assert.Equal(2, encoder.RawSetCount);
                renderBodyCompleted = true;

                void DrawColor(Vector4 color)
                {
                    if (target == ShaderTarget.SpirV)
                    {
                        var root = new HelperColorRoot(color);
                        resources.Draw(encoder, in root, 3, rootParameter: "arguments", shaderTypeName: "HelperColorRoot");
                        root = new HelperColorRoot(new(0, 0, 1, 1));
                    }
                    else
                    {
                        var root = new UniformColorRoot(color);
                        resources.Draw(encoder, in root, 3, rootParameter: "arguments", shaderTypeName: "HelperColorRoot");
                        root = new UniformColorRoot(new(0, 0, 1, 1));
                    }
                }
            }
            finally
            {
                try
                {
                    encoder.End();
                }
                catch when (!renderBodyCompleted)
                {
                    // Preserve the recording failure rather than replacing it with a cleanup failure.
                }
            }

            commands.Barrier(new TextureBarrierDesc
            {
                Texture = texture,
                Range = new(0, 1, 0, 1),
                BeforeState = TextureState.ColorAttachment,
                AfterState = TextureState.CopySource,
                Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite),
                After = new(PipelineStage.Copy, ResourceAccess.CopyRead),
            });
            commands.CopyTextureToBuffer(new() { Texture = texture, Width = 2, Height = 1 }, new() { Buffer = readback.SliceElements(0, pitch), BytesPerRow = pitch, RowsPerImage = 1 });
            commands.Barrier(new BufferBarrierDesc<byte>
            {
                Buffer = readback.SliceElements(0, pitch),
                Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
                After = new(PipelineStage.Host, ResourceAccess.HostRead),
            });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            await submission.WaitAsync(timeout.Token);
            await readback.MapAsync(timeout.Token);
            byte[] pixels = new byte[pitch];
            readback.CopyTo(pixels);
            readback.Unmap();
            Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, pixels[..8]);
            commandsCompleted = true;
        }
        finally
        {
            try
            {
                commands.Dispose();
            }
            catch when (!commandsCompleted)
            {
                // An End failure can leave the pass active; disposal must not hide the original error.
            }
        }
    }

    private readonly record struct HelperColorRoot(float Red, float Green, float Blue, float Alpha, uint Gain)
    {
        internal HelperColorRoot(Vector4 color)
            : this(color.X, color.Y, color.Z, color.W, 1)
        {
        }
    }

    private readonly record struct UniformColorRoot(Vector4 Color);

    // The adapter only observes overload selection: all actual recording and root allocation are native.
    // The old helper's eager Capture reaches the rejecting snapshot overload before any draw occurs.
    private sealed class StrictNativeEncoder(IRenderEncoder native) : IRenderEncoder
    {
        internal int RawSetCount { get; private set; }

        public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
            where T : unmanaged
        {
            RawSetCount++;
            native.SetArguments(in arguments, rootParameter, shaderTypeName);
        }

        public void SetArguments<T>(in T arguments)
            where T : IShaderArguments => throw new InvalidOperationException("An ordinary unmanaged root has no interface serializer.");

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => native.SetArgumentValidation(prepare);

        public void SetArgumentTable(IArgumentTable table) => native.SetArgumentTable(table);

        public void SetPipeline(IGraphicsPipeline pipeline) => native.SetPipeline(pipeline);

        public void SetRenderState(GraphicsRenderStateDesc state) => native.SetRenderState(state);

        public void SetViewport(Viewport viewport) => native.SetViewport(viewport);

        public void SetScissor(ScissorRect scissor) => native.SetScissor(scissor);

        public void SetBlendConstant(BlendConstant value) => native.SetBlendConstant(value);

        public void SetStencilReference(uint reference) => native.SetStencilReference(reference);

        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => native.Draw(vertexCount, instanceCount, firstVertex, firstInstance);

        public void SetIndexBuffer(BufferSlice<ushort> indices) => native.SetIndexBuffer(indices);

        public void SetIndexBuffer(BufferSlice<uint> indices) => native.SetIndexBuffer(indices);

        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => native.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);

        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments) => native.DrawIndirect(arguments);

        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments) => native.DrawIndexedIndirect(arguments);

        public void End() => native.End();
    }
}
