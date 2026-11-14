using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks typed callback arguments through actual native shader execution and readback.</summary>
public sealed class TypedGraphArgumentsTests
{
    private const string Source = """
        #include "lumyte.slang"
        struct NativeGraphArguments
        {
            GpuRWBufferRef<uint> Output;
            uint Index;
            uint Value;
        };
        ConstantBuffer<NativeGraphArguments> arguments;
        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main() { arguments.Output.Store(arguments.Index, arguments.Value); }
        """;

    /// <summary>Vulkan freezes ordinary typed values and validates the selected native pipeline at execution.</summary>
    /// <returns>The native submission and readback completion.</returns>
    [GpuFact]
    public async Task VulkanTypedCallbackArgumentsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckAsync(device);
    }

    /// <summary>Wgpu freezes ordinary typed values and validates the selected native pipeline at execution.</summary>
    /// <returns>The native submission and readback completion.</returns>
    [GpuFact]
    public async Task WgpuTypedCallbackArgumentsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckAsync(device);
    }

    private static async Task CheckAsync(IGraphicDevice device)
    {
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = Source, Stage = ShaderStage.Compute, Target = device.Caps.ShaderTarget });
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using var graph = new Graph(device);
        RenderGraphBuffer<uint> destination = graph.ImportBuffer(output, default);
        RenderGraphBuffer<uint> result = graph.ImportBuffer(readback, default);
        graph.AddPass("typed compute", context =>
        {
            var arguments = new NativeGraphArguments(context.GetGpuRef(destination, 0, 2), 0, 7);
            using RenderGraphComputeEncoder compute = context.BeginComputePass();
            compute.SetPipeline(pipeline);
            compute.SetArguments(in arguments);
            arguments = arguments with { Value = 99 };
            compute.Dispatch(1);
            compute.Dispatch(1);
            arguments = arguments with { Index = 1, Value = 11 };
            compute.SetArguments(in arguments);
            arguments = arguments with { Value = 99 };
            compute.Dispatch(1);
        }).UseBuffer(destination, new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite));
        graph.AddPass("read typed values", context => context.Commands.CopyBuffer(context.GetBuffer(destination).SliceElements(0, 2), context.GetBuffer(result).SliceElements(0, 2)))
            .UseBuffer(destination, new(PipelineStage.Copy, ResourceAccess.CopyRead))
            .UseBuffer(result, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        using CompiledRenderGraph plan = graph.Compile();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        plan.Record(commands);
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        uint[] values = new uint[2];
        readback.CopyTo(values);
        readback.Unmap();
        Assert.Equal(new uint[] { 7, 11 }, values);
        CheckDisposedPipeline(device, shader, output);
    }

    private static void CheckDisposedPipeline(IGraphicDevice device, IGraphicsShader shader, IGraphicsBuffer<uint> output)
    {
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using var graph = new Graph(device);
        RenderGraphBuffer<uint> destination = graph.ImportBuffer(output, default);
        graph.AddPass("disposed program", context =>
        {
            var arguments = new NativeGraphArguments(context.GetGpuRef(destination, 0, 2), 0, 1);
            using RenderGraphComputeEncoder compute = context.BeginComputePass();
            compute.SetPipeline(pipeline);
            compute.SetArguments(in arguments);
            pipeline.Dispose();
            Assert.Throws<ObjectDisposedException>(() => compute.Dispatch(1));
        }).UseBuffer(destination, new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite)).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        plan.Record(commands);

        // The invalid execution was never emitted or submitted.
    }

    private record struct NativeGraphArguments(IGpuRef<uint> Output, uint Index, uint Value) : IShaderArguments
    {
        public static string ShaderTypeName => "NativeGraphArguments";

        public static int TransferSize => 24;

        public static int ReferenceCount => 1;

        public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Output", 0, 16, typeof(ShaderReferenceSlot), true), new("Index", 16, 4, typeof(uint)), new("Value", 20, 4, typeof(uint))];

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            Wire wire = new() { Index = Index, Value = Value };
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
            writer.WriteReference(Output);
            writer.WriteValue(bytes[16..]);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 24)]
        private struct Wire
        {
            [System.Runtime.InteropServices.FieldOffset(0)]
            public ShaderReferenceSlot Output;

            [System.Runtime.InteropServices.FieldOffset(16)]
            public uint Index;

            [System.Runtime.InteropServices.FieldOffset(20)]
            public uint Value;
        }
    }
}
