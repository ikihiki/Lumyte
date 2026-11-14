using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>One generated IShaderArguments struct works at both public transfer boundaries.</summary>
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
        void main(uint3 tid : SV_DispatchThreadID)
        {
            UnifiedTransferValue value = arguments.Values.Load(tid.x);
            value.Output.Store(0, value.Value);
        }
        """;

    /// <summary>Vulkan consumes the same struct as a root and a GPU-loaded typed element.</summary>
    /// <returns>The completed readback checks in both validation modes.</returns>
    [GpuFact]
    public async Task VulkanSameStructSupportsRootAndTypedBufferAsync()
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
            await CheckMiddleElementAsync(device, buffer);
        }
    }

    /// <summary>Wgpu consumes the same struct as a root and a GPU-loaded typed element.</summary>
    /// <returns>The completed readback checks in both validation modes.</returns>
    [GpuFact]
    public async Task WgpuSameStructSupportsRootAndTypedBufferAsync()
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
            await CheckMiddleElementAsync(device, buffer);
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

        UnifiedTransferValue uploaded = value;
        uploaded.Output = value.Output!.GetElement(1);
        upload.CopyFrom(new[] { uploaded });
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
        await CheckGraphAutomaticBindingAsync(device, uniformPipeline);
    }

    private static async Task CheckMiddleElementAsync(IGraphicDevice device, ShaderArtifact artifact)
    {
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsShaderDataBuffer<UnifiedTransferValue> upload = device.CreateShaderDataBuffer<UnifiedTransferValue>(artifact, 3, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<UnifiedTransferValue> data = device.CreateShaderDataBuffer<UnifiedTransferValue>(artifact, 3);
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 2 });
        IGpuRef<uint> outputs = table.WriteBuffer(0, output.SliceElements(0, 4), ShaderBufferAccess.ReadWrite);
        UnifiedTransferValue[] rows = [new() { Value = 11, Output = outputs.GetElement(0) }, new() { Value = 22, Output = outputs.GetElement(1) }, new() { Value = 33, Output = outputs.GetElement(2) }];
        var root = new UnifiedTransferReadRoot { Values = table.WriteBuffer(1, data.SliceElements(0, 3)) };
        await upload.MapAsync();
        Assert.Throws<ArgumentException>(() => upload.CopyFrom([default(UnifiedTransferValue)], elementOffset: 1));
        upload.CopyFrom(rows);
        upload.Unmap();
        using (IGraphicsCommandBuffer initial = device.CreateCommandBuffer(new()))
        {
            initial.Barrier(new ShaderDataBufferBarrierDesc<UnifiedTransferValue> { Buffer = upload.SliceElements(0, 3), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            initial.CopyBuffer(upload.SliceElements(0, 3), data.SliceElements(0, 3));
            initial.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([initial]);
            await submission.WaitAsync();
        }

        var destinationMetadata = (IShaderDataSource)data;
        IShaderReferenceMetadata destinationFirst = destinationMetadata.ReadReferences(0);
        IShaderReferenceMetadata destinationLast = destinationMetadata.ReadReferences(2);
        var uploadMetadata = (IShaderDataSource)upload;
        IShaderReferenceMetadata? first = uploadMetadata.ReadReferences(0);
        IShaderReferenceMetadata? last = uploadMetadata.ReadReferences(2);
        await upload.MapAsync();
        {
            ref UnifiedTransferValue middle = ref rows[1];
            middle.Value = 77;
            middle.Output = outputs.GetElement(3);
            upload.CopyFrom(rows.AsSpan(1, 1), elementOffset: 1);
        }

        upload.Unmap();
        Assert.Same(first, uploadMetadata.ReadReferences(0));
        Assert.Same(last, uploadMetadata.ReadReferences(2));
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new ShaderDataBufferBarrierDesc<UnifiedTransferValue> { Buffer = upload.SliceElements(1, 1), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.Barrier(new ShaderDataBufferBarrierDesc<UnifiedTransferValue> { Buffer = data.SliceElements(1, 1), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
        commands.CopyBuffer(upload.SliceElements(1, 1), data.SliceElements(1, 1));
        commands.Barrier(new ShaderDataBufferBarrierDesc<UnifiedTransferValue> { Buffer = data.SliceElements(0, 3), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        IComputeEncoder encoder = commands.BeginComputePass(new());
        encoder.SetPipeline(pipeline);
        encoder.SetArgumentTable(table);
        encoder.SetArguments(in root);
        encoder.Dispatch(3);
        encoder.End();
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.SliceElements(0, 4), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(output.SliceElements(0, 4), readback.SliceElements(0, 4));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, 4), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission complete = device.Queue.Submit([commands]);
        await complete.WaitAsync();
        Assert.Same(destinationFirst, destinationMetadata.ReadReferences(0));
        Assert.Same(destinationLast, destinationMetadata.ReadReferences(2));
        IShaderReference replacement = Assert.Single(destinationMetadata.ReadReferences(1).References);
        Assert.Same(((IShaderReference)rows[1].Output!).Resource, replacement.Resource);
        Assert.Equal(((IShaderReference)rows[1].Output!).OffsetInBytes, replacement.OffsetInBytes);
        await readback.MapAsync();
        uint[] actual = new uint[4];
        readback.CopyTo(actual);
        readback.Unmap();
        Assert.Equal(11u, actual[0]);
        Assert.Equal(33u, actual[2]);
        Assert.Equal(77u, actual[3]);
    }

    private static async Task CheckGraphAutomaticBindingAsync(IGraphicDevice device, IGraphicsComputePipeline pipeline)
    {
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using var graph = new global::Lumyte.Graphics.RenderGraph.RenderGraph(device);
        RenderGraphBuffer<uint> target = graph.ImportBuffer(output, default);
        graph.AddPass("same struct automatically binds graph root", context =>
        {
            var value = new UnifiedTransferValue { Value = 29, Output = context.GetGpuRef(target) };
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(in value);
            value.Value = 99;
            encoder.Dispatch(1);
        }).UseBuffer(target, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite)).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        plan.Record(commands);
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.SliceElements(0, 1), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(output.SliceElements(0, 1), readback.SliceElements(0, 1));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, 1), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await submission.WaitAsync(timeout.Token);
        await readback.MapAsync(timeout.Token);
        try
        {
            uint[] actual = new uint[1];
            readback.CopyTo(actual);
            Assert.Equal(29u, actual[0]);
        }
        finally
        {
            readback.Unmap();
        }
    }

    private static async Task CheckGraphCopyAsync(IGraphicDevice device, ShaderArtifact artifact, IGraphicsShaderDataBuffer<UnifiedTransferValue> upload)
    {
        using var graph = new global::Lumyte.Graphics.RenderGraph.RenderGraph(device);
        RenderGraphShaderDataBuffer<UnifiedTransferValue> source = graph.ImportShaderDataBuffer(upload, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        RenderGraphShaderDataBuffer<UnifiedTransferValue> destination = graph.CreateShaderDataBuffer<UnifiedTransferValue>(artifact, 1);
        bool recorded = false;
        graph.AddPass("same IShaderArguments struct typed copy", context =>
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
