using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Validation;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>One writable physical binding serves both logical shader helper classes.</summary>
public sealed class ReadWriteReservationGpuTests
{
    private const string Source = """
        #include "lumyte.slang"
        struct ReadWriteRoot { GpuBufferRef<uint> Read; GpuRWBufferRef<uint> Write; };
        ConstantBuffer<ReadWriteRoot> arguments;
        [numthreads(1, 1, 1)]
        void main() { uint value = arguments.Read.Load(0); arguments.Write.Store(0, value * 7 + arguments.Read.Load(1) * 3); }
        """;

    /// <summary>Runs the native Vulkan path with and without the managed validation wrapper.</summary>
    /// <returns>The completed native readback checks.</returns>
    [Trait("Execution", "Gpu")]
    [GpuFact]
    public async Task VulkanSameReservationReadAndWriteAsync()
    {
        using var raw = VulkanDevice.Create();
        await ExecuteAsync(raw, ShaderTarget.SpirV);
        using var validatedRaw = VulkanDevice.Create();
        using var wrapped = new ValidationDevice(validatedRaw);
        await ExecuteAsync(wrapped, ShaderTarget.SpirV);
    }

    /// <summary>Runs the native WebGPU path without creating overlapping storage aliases.</summary>
    /// <returns>The completed native readback checks.</returns>
    [Trait("Execution", "Gpu")]
    [GpuFact]
    public async Task WgpuSameReservationReadAndWriteAsync()
    {
        using var raw = WgpuDevice.Create();
        await ExecuteAsync(raw, ShaderTarget.Wgsl);
        using var validatedRaw = WgpuDevice.Create();
        using var wrapped = new ValidationDevice(validatedRaw);
        await ExecuteAsync(wrapped, ShaderTarget.Wgsl);
    }

    private static async Task ExecuteAsync(IGraphicDevice device, ShaderTarget target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var compiler = new SlangShaderCompiler(Environment.GetEnvironmentVariable("LUMYTE_SLANG_COMPILER") ?? "slangc");
        ShaderArtifact artifact = await compiler.CompileAsync(new() { Source = Source, Stage = ShaderStage.Compute, Target = target }, timeout.Token);
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsBuffer<uint> upload = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        await upload.MapAsync(timeout.Token);
        upload.CopyFrom(new uint[] { 11, 13 });
        upload.Unmap();
        using IGraphicsBuffer<uint> storage = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource | BufferUsage.CopyDestination });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using var plan = new ArgumentTablePlan(device);
        ShaderResources resources = plan.CreateShaderResources();
        plan.ConfigureExecution(resources, 0);
        IGpuRef<uint>? reference = null;
        resources.RegisterBuffer(storage.SliceElements(0, 2), value => reference = value, ArgumentBufferAccess.ReadWrite);
        plan.MaterializeResources([resources], timeout.Token);
        Assert.NotNull(reference);
        var root = new ReadWriteRoot(reference);
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = upload.SliceElements(0, 2), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(upload.SliceElements(0, 2), storage.SliceElements(0, 2));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = storage.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite) });
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(pipeline);
        resources.Dispatch(compute, in root, 1);
        compute.End();
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = storage.SliceElements(0, 2), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(storage.SliceElements(0, 2), readback.SliceElements(0, 2));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync(timeout.Token);
        await readback.MapAsync(timeout.Token);
        uint[] result = new uint[2];
        readback.CopyTo(result);
        readback.Unmap();
        Assert.Equal(new uint[] { 116, 13 }, result);

        // The graph derives one ReadWrite reservation from the exact combined range use.
        using var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(device);
        var scope = new BarrierScope(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite);
        Lumyte.Graphics.RenderGraph.RenderGraphBuffer<uint> logical = graph.ImportBuffer(storage, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        graph.AddPass("same-range-readwrite", context =>
        {
            var graphRoot = new ReadWriteRoot(context.GetGpuRef(logical, 0, 2));
            using Lumyte.Graphics.RenderGraph.RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(in graphRoot);
            encoder.Dispatch(1);
        }).UseBuffer(logical, 0, 2, scope);
        graph.ExportBuffer(logical, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        using Lumyte.Graphics.RenderGraph.CompiledRenderGraph compiled = graph.Compile();
        using IGraphicsCommandBuffer graphCommands = device.CreateCommandBuffer(new());
        compiled.Record(graphCommands);
        graphCommands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, 2), Before = new(PipelineStage.Host, ResourceAccess.HostRead), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
        graphCommands.CopyBuffer(storage.SliceElements(0, 2), readback.SliceElements(0, 2));
        graphCommands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        graphCommands.Finish();
        using IGraphicsSubmission graphSubmission = device.Queue.Submit([graphCommands]);
        await graphSubmission.WaitAsync(timeout.Token);
        await readback.MapAsync(timeout.Token);
        readback.CopyTo(result);
        readback.Unmap();
        Assert.Equal(new uint[] { 851, 13 }, result);
    }

    private readonly struct ReadWriteRoot(IGpuRef<uint> reference) : IShaderArguments
    {
        public static string ShaderTypeName => "ReadWriteRoot";

        public static int TransferSize => 32;

        public static int ReferenceCount => 2;

        public static IReadOnlyList<ShaderTransferField> TransferFields { get; } =
        [
            new("Read", 0, 16, typeof(ShaderReferenceSlot), true),
            new("Write", 16, 16, typeof(ShaderReferenceSlot), true),
        ];

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            writer.WriteReference(reference);
            writer.WriteReference(reference);
        }
    }
}
