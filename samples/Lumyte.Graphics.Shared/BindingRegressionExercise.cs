using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Checks shader ABI layout, transitive references and registered ranges through common APIs.</summary>
internal static class BindingRegressionExercise
{
    internal static async Task RunAsync(IGraphicDevice device)
    {
        CheckIncompatibleStages(device);
        CheckCompatibleCyclicStages(device);
        await CheckLayoutsAsync(device);
        await CheckSeparateCyclesAsync(device);
        ulong alignment = checked(Math.Max(4, device.Caps.StorageBufferOffsetAlignment) * 4UL);
        ulong minimumSize = checked((alignment * 3) + 16);
        await CheckRangesAsync(device, minimumSize, alignment);

        // Exercise a small registration inside an allocation that cannot be bound in full.
        // Keep the allocation bounded on adapters whose storage binding limit is very large.
        ulong bindingLimit = device.Caps.MaxStorageBufferBindingSize;
        const ulong AllocationBudget = 256UL * 1024 * 1024;
        if (bindingLimit >= minimumSize && bindingLimit < AllocationBudget && bindingLimit < device.Caps.MaxBufferSize)
        {
            ulong allocationSize = checked(((bindingLimit / 4) + 1) * 4);
            if (allocationSize <= device.Caps.MaxBufferSize)
            {
                await CheckRangesAsync(device, allocationSize, alignment);
            }
        }
    }

    private static ShaderArtifact Artifact(string name) => ShaderArtifact.LoadEmbedded(typeof(BindingRegressionExercise).Assembly, "Lumyte.Shaders." + name + ".lshader");

    private static void CheckIncompatibleStages(IGraphicDevice device)
    {
        using IGraphicsShader vertex = device.CreateShader(Artifact("binding-mismatch-vertex"));
        using IGraphicsShader fragment = device.CreateShader(Artifact("binding-mismatch-fragment"));
        try
        {
            using IGraphicsPipeline unexpected = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Matching root schemas concealed incompatible material layouts behind an intermediate GPU reference.");
    }

    private static void CheckCompatibleCyclicStages(IGraphicDevice device)
    {
        using IGraphicsShader vertex = device.CreateShader(Artifact("binding-cycle-vertex"));
        using IGraphicsShader fragment = device.CreateShader(Artifact("binding-cycle-fragment"));
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
    }

    private static async Task CheckLayoutsAsync(IGraphicDevice device)
    {
        ShaderArtifact artifact = Artifact("binding-layout");
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsShaderDataBuffer<BindingVector> vectors = device.CreateBuffer<BindingVector>(artifact, 2);
        using IGraphicsShaderDataBuffer<BindingPackedVector> packed = device.CreateBuffer<BindingPackedVector>(artifact, 2);
        using IGraphicsShaderDataBuffer<BindingNestedVector> nested = device.CreateBuffer<BindingNestedVector>(artifact, 2);
        using IGraphicsShaderDataBuffer<BindingVector> vectorStaging = device.CreateBuffer<BindingVector>(artifact, 2, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<BindingPackedVector> packedStaging = device.CreateBuffer<BindingPackedVector>(artifact, 2, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<BindingNestedVector> nestedStaging = device.CreateBuffer<BindingNestedVector>(artifact, 2, MemoryPreference.Upload);
        await WriteAsync(vectorStaging, [new(new(11, 12, 13)), new(new(21, 22, 23))]);
        await WriteAsync(packedStaging, [new(31, new(32, 33, 34)), new(41, new(42, 43, 44))]);
        await WriteAsync(nestedStaging, [new(new(new(51, 52, 53)), 54), new(new(new(61, 62, 63)), 64)]);
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 22, Usage = BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 22, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 4 });
        var arguments = new BindingLayoutArguments(
            table.WriteBuffer(0, vectors.SliceElements(0, 2)),
            table.WriteBuffer(1, packed.SliceElements(0, 2)),
            table.WriteBuffer(2, nested.SliceElements(0, 2)),
            table.WriteBuffer(3, output.Slice(0, output.Count)));
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        RecordUpload(commands, vectorStaging, vectors);
        RecordUpload(commands, packedStaging, packed);
        RecordUpload(commands, nestedStaging, nested);
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(pipeline);
        compute.SetArgumentTable(table);
        compute.SetArguments(in arguments);
        compute.Dispatch(1);
        compute.End();
        uint[] values = await ReadOutputAsync(device, commands, output, readback);
        uint[] expected = [11, 12, 13, 21, 22, 23, 31, 32, 33, 34, 41, 42, 43, 44, 51, 52, 53, 54, 61, 62, 63, 64];
        Require(values.AsSpan().SequenceEqual(expected), "Shader data vector, scalar/vector or nested member offsets and array strides do not match shader loads.");
    }

    private static async Task CheckSeparateCyclesAsync(IGraphicDevice device)
    {
        ShaderArtifact artifact = Artifact("binding-compute");
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });

        // Multiplying an element stride by the required byte alignment gives an aligned, nonzero registration.
        ulong elementOffset = device.Caps.StorageBufferOffsetAlignment;
        using IGraphicsShaderDataBuffer<BindingNode> first = device.CreateBuffer<BindingNode>(artifact, elementOffset + 1);
        using IGraphicsShaderDataBuffer<BindingNode> second = device.CreateBuffer<BindingNode>(artifact, elementOffset + 1);
        using IGraphicsShaderDataBuffer<BindingNode> firstStaging = device.CreateBuffer<BindingNode>(artifact, 1, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<BindingNode> secondStaging = device.CreateBuffer<BindingNode>(artifact, 1, MemoryPreference.Upload);
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 3 });
        IGpuRef<BindingNode> a = table.WriteBuffer(0, first.SliceElements(elementOffset, 1));
        IGpuRef<BindingNode> b = table.WriteBuffer(1, second.SliceElements(elementOffset, 1));
        IGpuRef<uint> result = table.WriteBuffer(2, output.Slice(0, 1));
        await WriteAsync(firstStaging, [new(3, b)]);
        await WriteAsync(secondStaging, [new(7, a)]);
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        RecordUpload(commands, firstStaging, first, elementOffset);
        RecordUpload(commands, secondStaging, second, elementOffset);
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(pipeline);
        compute.SetArgumentTable(table);
        var arguments = new BindingComputeArguments(default, 0, a, result);
        compute.SetArguments(in arguments);
        compute.Dispatch(1);
        compute.End();
        uint[] values = await ReadOutputAsync(device, commands, output, readback);
        Require(values[0] == 13, "A reference stored in shader data did not follow the cycle across separate buffer allocations.");
    }

    private static async Task CheckRangesAsync(IGraphicDevice device, ulong allocationSize, ulong alignment)
    {
        using IGraphicsShader shader = device.CreateShader(Artifact("binding-ranges"));
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsBuffer<uint> storage = device.CreateBuffer<uint>(new() { Count = allocationSize / 4, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        using IGraphicsBuffer<uint> staging = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        await staging.MapAsync();
        staging.CopyFrom([11, 13, 17, 19, 23, 29, 31, 37]);
        staging.Unmap();
        ulong firstOffset = alignment / 4;
        ulong secondOffset = alignment / 2;
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 3 });
        IGpuRef<uint> first = table.WriteBuffer(0, storage.Slice(firstOffset, 4));
        IGpuRef<uint> second = table.WriteBuffer(1, storage.Slice(secondOffset, 4));
        var arguments = new BindingRangeArguments(first.GetElement(1), second.GetElement(2), table.WriteBuffer(2, output.Slice(0, 1)));
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = staging.Slice(0, 8), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(staging.Slice(0, 4), storage.Slice(firstOffset, 4));
        commands.CopyBuffer(staging.Slice(4, 4), storage.Slice(secondOffset, 4));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = storage.Slice(firstOffset, 4), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = storage.Slice(secondOffset, 4), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(pipeline);
        compute.SetArgumentTable(table);
        compute.SetArguments(in arguments);
        compute.Dispatch(1);
        compute.End();
        uint[] values = await ReadOutputAsync(device, commands, output, readback);
        Require(values[0] == 44, "Separate registered ranges or their selected element offsets did not resolve within the backing allocation.");
    }

    private static async Task WriteAsync<T>(IGraphicsShaderDataBuffer<T> staging, T[] values)
        where T : struct, IShaderData
    {
        await staging.MapAsync();
        staging.CopyFrom(values);
        staging.Unmap();
    }

    private static void RecordUpload<T>(IGraphicsCommandBuffer commands, IGraphicsShaderDataBuffer<T> staging, IGraphicsShaderDataBuffer<T> gpu, ulong destinationOffset = 0)
        where T : struct, IShaderData
    {
        commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = staging.SliceElements(0, staging.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(staging.SliceElements(0, staging.Count), gpu.SliceElements(destinationOffset, staging.Count));
        commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = gpu.SliceElements(destinationOffset, staging.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
    }

    private static async Task<uint[]> ReadOutputAsync(IGraphicDevice device, IGraphicsCommandBuffer commands, IGraphicsBuffer<uint> output, IGraphicsBuffer<uint> readback)
    {
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.Slice(0, output.Count), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(output.Slice(0, output.Count), readback.Slice(0, readback.Count));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.Slice(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        uint[] values = new uint[checked((int)readback.Count)];
        readback.CopyTo(values);
        readback.Unmap();
        return values;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
