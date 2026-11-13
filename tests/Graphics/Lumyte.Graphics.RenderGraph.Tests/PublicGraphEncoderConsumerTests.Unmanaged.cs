using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Checks ordinary unmanaged public graph arguments and storage.</summary>
public sealed partial class PublicGraphEncoderConsumerTests
{
    /// <summary>A nonpartial struct with no interfaces snapshots directly through the graph encoder.</summary>
    [Fact]
    public void OrdinaryUnmanagedGraphRootNeedsNoCodec()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        using var pipeline = new PublicGraphConsumerComputePipeline(Artifact(ShaderStage.Compute));
        graph.AddPass("plain numeric", context =>
        {
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            var arguments = new PlainInputs { Float = 17 };
            encoder.SetArguments(in arguments, shaderTypeName: "ConsumerInputs");
            arguments.Float = 29;
            encoder.Dispatch(1);
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(17f, MemoryMarshal.Read<float>(Assert.Single(commands.Encoder.Executions).Data.Span));
    }

    /// <summary>External unmanaged types use plan-owned raw buffers and existing graph declarations.</summary>
    [Fact]
    public void OrdinaryUnmanagedGraphStorageUsesRawBufferPath()
    {
        var device = new TestDevice();
        using var graph = new RenderGraph(device);
        RenderGraphBuffer<Vector3> buffer = graph.CreateShaderDataBuffer<Vector3>(Artifact(ShaderStage.Compute), 2);
        graph.AddPass("initialize", context =>
        {
            IGraphicsBuffer<Vector3> allocation = context.GetBuffer(buffer);
            Assert.Equal(24UL, allocation.SizeInBytes);
            Assert.Equal(12UL, allocation.Layout.ElementStrideInBytes);
        }).UseBuffer(buffer, new(PipelineStage.Copy, ResourceAccess.CopyWrite)).KeepAlive();
        Assert.Equal(0, device.Buffers);
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        Assert.Equal(1, device.Buffers);
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PlainInputs
    {
        [FieldOffset(0)]
        public float Float;

        [FieldOffset(4)]
        public int Int;

        [FieldOffset(8)]
        public uint UInt;

        [FieldOffset(16)]
        public Vector2 Vector2;

        [FieldOffset(32)]
        public Vector3 Vector3;

        [FieldOffset(48)]
        public Vector4 Vector4;

        [FieldOffset(64)]
        public Matrix4x4 Matrix;
    }
}
