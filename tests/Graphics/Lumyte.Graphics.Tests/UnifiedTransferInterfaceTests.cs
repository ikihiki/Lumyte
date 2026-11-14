using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>One generated IShaderArguments class works at both public transfer boundaries.</summary>
public sealed class UnifiedTransferInterfaceTests
{
    private const string ValueSource = """
        #include "lumyte.slang"
        struct UnifiedTransferValue
        {
            uint Value;
            GpuRWBufferRef<uint> Output;
        };
        """;

    private const string UniformSource = ValueSource + """

        ConstantBuffer<UnifiedTransferValue> arguments;
        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main()
        {
            arguments.Output.Store(0, arguments.Value);
        }
        """;

    private const string BufferSource = ValueSource + """

        struct UnifiedTransferReadRoot
        {
            GpuBufferRef<UnifiedTransferValue> Values;
        };
        ConstantBuffer<UnifiedTransferReadRoot> arguments;
        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main()
        {
            UnifiedTransferValue value = arguments.Values.Load(0);
            value.Output.Store(1, value.Value);
        }
        """;

    /// <summary>Vulkan consumes the same class as a root and a GPU-loaded typed element.</summary>
    /// <returns>The completed readback checks in both validation modes.</returns>
    [GpuFact]
    public async Task VulkanSameClassSupportsRootAndTypedBufferAsync()
    {
        (ShaderArtifact uniform, ShaderArtifact buffer) = await CompileAsync(ShaderTarget.SpirV);
        foreach (bool validation in new[] { false, true })
        {
            using var device = VulkanDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await CheckAsync(device, uniform, buffer);
        }
    }

    /// <summary>Wgpu consumes the same class as a root and a GPU-loaded typed element.</summary>
    /// <returns>The completed readback checks in both validation modes.</returns>
    [GpuFact]
    public async Task WgpuSameClassSupportsRootAndTypedBufferAsync()
    {
        (ShaderArtifact uniform, ShaderArtifact buffer) = await CompileAsync(ShaderTarget.Wgsl);
        foreach (bool validation in new[] { false, true })
        {
            using var device = WgpuDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await CheckAsync(device, uniform, buffer);
        }
    }

    private static async Task<(ShaderArtifact Uniform, ShaderArtifact Buffer)> CompileAsync(ShaderTarget target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var compiler = new SlangShaderCompiler();
        ShaderArtifact uniform = await compiler.CompileAsync(new() { Source = UniformSource, Target = target, Stage = ShaderStage.Compute }, timeout.Token);
        ShaderArtifact buffer = await compiler.CompileAsync(new() { Source = BufferSource, Target = target, Stage = ShaderStage.Compute }, timeout.Token);
        return (uniform, buffer);
    }

    private static async Task CheckAsync(IGraphicDevice device, ShaderArtifact uniformArtifact, ShaderArtifact bufferArtifact)
    {
        using IGraphicsShader uniformShader = device.CreateShader(uniformArtifact);
        using IGraphicsShader bufferShader = device.CreateShader(bufferArtifact);
        using IGraphicsComputePipeline uniformPipeline = device.CreateComputePipeline(new() { ComputeShader = uniformShader });
        using IGraphicsComputePipeline bufferPipeline = device.CreateComputePipeline(new() { ComputeShader = bufferShader });
        using IGraphicsShaderDataBuffer<UnifiedTransferValue> upload = device.CreateShaderDataBuffer<UnifiedTransferValue>(bufferArtifact, 1, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<UnifiedTransferValue> values = device.CreateShaderDataBuffer<UnifiedTransferValue>(bufferArtifact, 1);
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 2 });
        BufferSlice<uint> outputRange = output.SliceElements(0, 2);
        BufferSlice<uint> readbackRange = readback.SliceElements(0, 2);
        ShaderDataSlice<UnifiedTransferValue> source = upload.SliceElements(0, 1);
        ShaderDataSlice<UnifiedTransferValue> destination = values.SliceElements(0, 1);
        var value = new UnifiedTransferValue { Value = 7, Output = table.WriteBuffer(0, outputRange, ShaderBufferAccess.ReadWrite) };
        var readRoot = new UnifiedTransferReadRoot { Values = table.WriteBuffer(1, destination) };

        using (var mappingTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await upload.MapAsync(mappingTimeout.Token);
        }

        upload.CopyFrom(new[] { value });
        upload.Unmap();
        value.Value = 13;

        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new ShaderDataBufferBarrierDesc<UnifiedTransferValue> { Buffer = source, Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(source, destination);
        commands.Barrier(new ShaderDataBufferBarrierDesc<UnifiedTransferValue> { Buffer = destination, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        IComputeEncoder uniform = commands.BeginComputePass(new());
        try
        {
            uniform.SetPipeline(uniformPipeline);
            uniform.SetArgumentTable(table);
            uniform.SetArguments(in value);
            value.Value = 99; // The selected root owns 13, not this later mutation.
            uniform.Dispatch(1);
        }
        finally
        {
            uniform.End();
        }

        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = outputRange, Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite) });
        IComputeEncoder typed = commands.BeginComputePass(new());
        try
        {
            typed.SetPipeline(bufferPipeline);
            typed.SetArgumentTable(table);
            typed.SetArguments(in readRoot);
            typed.Dispatch(1);
        }
        finally
        {
            typed.End();
        }

        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = outputRange, Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(outputRange, readbackRange);
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readbackRange, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        using (var completionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await submission.WaitAsync(completionTimeout.Token);
        }

        using (var mappingTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await readback.MapAsync(mappingTimeout.Token);
        }

        try
        {
            uint[] actual = new uint[2];
            readbackRange.CopyTo(actual);
            Assert.Equal(new uint[] { 13, 7 }, actual);
        }
        finally
        {
            readback.Unmap();
        }

        await CheckGraphCopyAsync(device, bufferArtifact, upload);
    }

    private static async Task CheckGraphCopyAsync(IGraphicDevice device, ShaderArtifact artifact, IGraphicsShaderDataBuffer<UnifiedTransferValue> upload)
    {
        using var graph = new global::Lumyte.Graphics.RenderGraph.RenderGraph(device);
        RenderGraphShaderDataBuffer<UnifiedTransferValue> source = graph.ImportShaderDataBuffer(upload, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        RenderGraphShaderDataBuffer<UnifiedTransferValue> destination = graph.CreateShaderDataBuffer<UnifiedTransferValue>(artifact, 1);
        bool recorded = false;
        graph.AddPass("same IShaderArguments class typed copy", context =>
        {
            IGraphicsShaderDataBuffer<UnifiedTransferValue> buffer = context.GetShaderDataBuffer(destination);
            Assert.Equal(1ul, buffer.Count);
            context.Commands.CopyBuffer(context.GetShaderDataBuffer(source).SliceElements(0, 1), buffer.SliceElements(0, 1));
            recorded = true;
        }).UseShaderDataBuffer(source, new(PipelineStage.Copy, ResourceAccess.CopyRead))
          .UseShaderDataBuffer(destination, new(PipelineStage.Copy, ResourceAccess.CopyWrite), discard: true)
          .KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        plan.Record(commands);
        Assert.True(recorded);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await submission.WaitAsync(timeout.Token);
    }
}
