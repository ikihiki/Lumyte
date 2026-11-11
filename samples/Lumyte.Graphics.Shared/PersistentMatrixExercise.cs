using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

internal static class PersistentMatrixExercise
{
    internal static async Task RunAsync(IGraphicDevice device)
    {
        var artifact = ShaderArtifact.LoadEmbedded(typeof(BindingMatrixData).Assembly, "Lumyte.Shaders.binding-persistent.lshader");
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsShaderDataBuffer<BindingNode> nodes = device.CreateShaderDataBuffer<BindingNode>(artifact, 1);
        using IGraphicsShaderDataBuffer<BindingNode> nodeStaging = device.CreateShaderDataBuffer<BindingNode>(artifact, 1, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<BindingMatrixData> matrices = device.CreateShaderDataBuffer<BindingMatrixData>(artifact, 1);
        using IGraphicsShaderDataBuffer<BindingMatrixData> matrixStaging = device.CreateShaderDataBuffer<BindingMatrixData>(artifact, 1, MemoryPreference.Upload);
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 6, Usage = BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 6, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 3 });
        IGpuRef<BindingNode> node = table.WriteBuffer(0, nodes.SliceElements(0, 1));
        IGpuRef<BindingMatrixData> matrix = table.WriteBuffer(1, matrices.SliceElements(0, 1));
        IGpuRef<uint> result = table.WriteBuffer(2, output.SliceElements(0, 6));
        BindingNode.Storage nodeValues = BindingNode.CreateStorage(nodeStaging, [new(0, node)]);
        nodeValues[0].Value = 9u;
        BindingMatrixData.Storage values = BindingMatrixData.CreateStorage(matrixStaging, [new(default, default, node)]);
        Matrix4x4 transform = new(2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53);
        values[0].Transform = transform;
        values[0].Child.Value = new Vector3(5, 7, 11);
        values[0].Node = node;
        await nodeStaging.MapAsync();
        nodeStaging.CopyFrom(nodeValues);
        nodeStaging.Unmap();
        await matrixStaging.MapAsync();
        matrixStaging.CopyFrom(values);
        matrixStaging.Unmap();
        values[0].Transform = Matrix4x4.Identity;
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        Upload(commands, nodeStaging, nodes);
        Upload(commands, matrixStaging, matrices);
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(pipeline);
        compute.SetArgumentTable(table);
        compute.SetArguments(new BindingMatrixArguments(matrix, result));
        compute.Dispatch(1);
        compute.End();
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.SliceElements(0, 6), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(output.SliceElements(0, 6), readback.SliceElements(0, 6));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, 6), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        uint[] actual = new uint[6];
        readback.CopyTo(actual);
        readback.Unmap();
        uint[] expected = [134, 159, 179, 209, 32, 9];
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException("Persistent matrix orientation, nested values or recursive shader references differ from analytic results.");
        }
    }

    private static void Upload<T>(IGraphicsCommandBuffer commands, IGraphicsShaderDataBuffer<T> source, IGraphicsShaderDataBuffer<T> destination)
        where T : struct, IShaderData
    {
        commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = source.SliceElements(0, 1), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(source.SliceElements(0, 1), destination.SliceElements(0, 1));
        commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = destination.SliceElements(0, 1), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
    }
}
