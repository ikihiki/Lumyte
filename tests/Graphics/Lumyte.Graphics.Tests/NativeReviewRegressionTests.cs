using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Review regressions for native preparation, indexed execution and typed device copies.</summary>
public sealed class NativeReviewRegressionTests
{
    /// <summary>Task-only roots participate in the same preparation ABI selection as mesh and fragment roots.</summary>
    [Fact]
    public void TaskOnlyRootParticipatesInPreparationAbi()
    {
        MethodInfo select = typeof(VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanCommandBuffer", true)!
            .GetMethod("PreparationRootTarget", BindingFlags.NonPublic | BindingFlags.Static)!;
        ShaderTargetData Empty(ShaderStage stage) => new(ShaderTarget.SpirV, stage, "main", "test", "row-major", [], """{"parameters":[]}""");
        ShaderTargetData task = Empty(ShaderStage.Task) with
        {
            ReflectionJson = """{"parameters":[{"name":"arguments","type":{"kind":"constantBuffer","elementType":{"kind":"struct","name":"NativeReviewValue","sizes":[{"kind":"uniform","value":4}],"fields":[]}}}]}""",
        };
        Assert.Same(task, Invoke(select, null, Empty(ShaderStage.Mesh), task, Empty(ShaderStage.Fragment), "arguments"));
        Assert.Same(task, Invoke(select, null, Empty(ShaderStage.Mesh), task, null, "arguments"));
        Assert.Throws<ArgumentException>(() => Invoke(select, null, Empty(ShaderStage.Mesh), null, null, "arguments"));
        Assert.Throws<ArgumentException>(() => Invoke(select, null, Empty(ShaderStage.Mesh), task, null, "missing"));
        Assert.Same(task, Invoke(select, null, task, null, null, "arguments"));
        Assert.Same(task, Invoke(select, null, Empty(ShaderStage.Mesh), null, task, "arguments"));
    }

    /// <summary>Vulkan rejects indexed indirect execution before native emission, with raw and prepared roots.</summary>
    /// <returns>The completed native checks.</returns>
    [GpuFact]
    [Trait("Execution", "Gpu")]
    public async Task VulkanUnsetIndexRejectedAsync() => await CheckIndexAsync(ShaderTarget.SpirV);

    /// <summary>Wgpu rejects indexed indirect execution before native emission, with raw and prepared roots.</summary>
    /// <returns>The completed native checks.</returns>
    [GpuFact]
    [Trait("Execution", "Gpu")]
    public async Task WgpuUnsetIndexRejectedAsync() => await CheckIndexAsync(ShaderTarget.Wgsl);

    /// <summary>Vulkan copies automatic typed storage and reads the copied numeric value on the GPU.</summary>
    /// <returns>The completed GPU readback.</returns>
    [GpuFact]
    [Trait("Execution", "Gpu")]
    public async Task VulkanAutomaticShaderDataCopyAsync() => await CheckCopyAsync(ShaderTarget.SpirV);

    /// <summary>Wgpu copies automatic typed storage and reads the copied numeric value on the GPU.</summary>
    /// <returns>The completed GPU readback.</returns>
    [GpuFact]
    [Trait("Execution", "Gpu")]
    public async Task WgpuAutomaticShaderDataCopyAsync() => await CheckCopyAsync(ShaderTarget.Wgsl);

    private static async Task CheckIndexAsync(ShaderTarget target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var compiler = new SlangShaderCompiler();
        ShaderArtifact vertex = await compiler.CompileAsync(new() { Source = "float4 main(uint i : SV_VertexID) : SV_Position { return float4(0,0,0,1); }", Stage = ShaderStage.Vertex, Target = target }, timeout.Token);
        ShaderArtifact fragment = await compiler.CompileAsync(new() { Source = "struct NativeReviewValue { uint Value; }; ConstantBuffer<NativeReviewValue> arguments; float4 main() : SV_Target { return float4(arguments.Value / 255.0,0,0,1); }", Stage = ShaderStage.Fragment, Target = target }, timeout.Token);
        using IDisposable owner = target == ShaderTarget.SpirV ? VulkanDevice.Create() : WgpuDevice.Create();
        var device = (IGraphicDevice)owner;
        using IGraphicsShader vs = device.CreateShader(vertex);
        using IGraphicsShader fs = device.CreateShader(fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vs, FragmentShader = fs });
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsBuffer<DrawIndexedIndirectArguments> indirect = device.CreateBuffer<DrawIndexedIndirectArguments>(new() { Count = 1, Usage = BufferUsage.Indirect });
        var state = GraphicsRenderStateDesc.ForColorTargets(1);
        var pass = new RenderPassDesc { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] };
        foreach (bool prepared in new[] { false, true })
        {
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            var value = new NativeReviewValue(17, prepared ? new object() : null);
            if (prepared)
            {
                commands.PrepareRender(pass, pipeline, state, null, in value);
            }

            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
            IRenderEncoder encoder = commands.BeginRenderPass(pass);
            encoder.SetPipeline(pipeline);
            encoder.SetRenderState(state);
            encoder.SetViewport(new(0, 0, 1, 1, 0, 1));
            encoder.SetScissor(new(0, 0, 1, 1));
            encoder.SetBlendConstant(default);
            encoder.SetStencilReference(0);
            encoder.SetArguments(in value);
            try
            {
                if (Environment.GetEnvironmentVariable("LUMYTE_NATIVE_REVIEW_SAFE_PROBE") == "1")
                {
                    // Baseline probes the exact common guard without emitting an invalid native draw.
                    MethodInfo prepare = encoder.GetType().GetMethod("PrepareDraw", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    object[] args = target == ShaderTarget.SpirV ? [true, false] : [true];
                    Assert.Throws<InvalidOperationException>(() => Invoke(prepare, encoder, args));
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => encoder.DrawIndexedIndirect(indirect.SliceElements(0, 1)));
                }
            }
            finally
            {
                encoder.End();
                commands.Finish();
            }
        }
    }

    private static async Task CheckCopyAsync(ShaderTarget target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        const string Source = "#include \"lumyte.slang\"\nstruct NativeReviewValue { uint Value; }; struct NativeReviewRoot { GpuBufferRef<NativeReviewValue> Values; GpuRWBufferRef<uint> Output; }; ConstantBuffer<NativeReviewRoot> arguments; [numthreads(1,1,1)] void main() { arguments.Output.Store(0, arguments.Values.Load(0).Value * 7 + arguments.Values.Load(1).Value); }";
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = Source, Stage = ShaderStage.Compute, Target = target }, timeout.Token);
        using IDisposable owner = target == ShaderTarget.SpirV ? VulkanDevice.Create() : WgpuDevice.Create();
        var device = (IGraphicDevice)owner;
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsShaderDataBuffer<NativeReviewValue> upload = device.CreateShaderDataBuffer<NativeReviewValue>(artifact, 2, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<NativeReviewValue> first = device.CreateShaderDataBuffer<NativeReviewValue>(artifact, 2);
        using IGraphicsShaderDataBuffer<NativeReviewValue> second = device.CreateShaderDataBuffer<NativeReviewValue>(artifact, 2);
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        Assert.Throws<InvalidOperationException>(() => first.MapAsync().GetAwaiter().GetResult());
        await upload.MapAsync(timeout.Token);
        upload.CopyFrom(new NativeReviewValue[] { new(11), new(13) });
        upload.Unmap();
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 2 });
        var root = new NativeReviewRoot(table.WriteBuffer(0, second.SliceElements(0, 2)), table.WriteBuffer(1, output.SliceElements(0, 1), ShaderBufferAccess.ReadWrite));
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new ShaderDataBufferBarrierDesc<NativeReviewValue> { Buffer = upload.SliceElements(0, 2), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(upload.SliceElements(0, 2), first.SliceElements(0, 2));
        commands.Barrier(new ShaderDataBufferBarrierDesc<NativeReviewValue> { Buffer = first.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(first.SliceElements(0, 2), second.SliceElements(0, 2));
        commands.Barrier(new ShaderDataBufferBarrierDesc<NativeReviewValue> { Buffer = second.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        IComputeEncoder encoder = commands.BeginComputePass(new());
        encoder.SetPipeline(pipeline);
        encoder.SetArgumentTable(table);
        encoder.SetArguments(in root);
        encoder.Dispatch(1);
        encoder.End();
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.SliceElements(0, 1), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(output.SliceElements(0, 1), readback.SliceElements(0, 1));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, 1), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync(timeout.Token);
        await readback.MapAsync(timeout.Token);
        uint[] actual = new uint[1];
        readback.CopyTo(actual);
        readback.Unmap();
        Assert.Equal(90u, actual[0]);
    }

    private static object? Invoke(MethodInfo method, object? target, params object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private readonly struct NativeReviewValue(uint value, object? identity = null) : IShaderArguments
    {
        public static string ShaderTypeName => "NativeReviewValue";

        public static int TransferSize => 4;

        public static int ReferenceCount => 0;

        public object? NativePreparationIdentity => identity;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
            => writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
    }

    private readonly struct NativeReviewRoot(IGpuRef<NativeReviewValue> values, IGpuRef<uint> output) : IShaderArguments
    {
        public static string ShaderTypeName => "NativeReviewRoot";

        public static int TransferSize => 32;

        public static int ReferenceCount => 2;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            writer.WriteReference(values);
            writer.WriteReference(output);
        }
    }
}
