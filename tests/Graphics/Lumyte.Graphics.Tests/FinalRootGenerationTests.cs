using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Real native execution checks selection-time ownership of final root generations.</summary>
public sealed class FinalRootGenerationTests
{
    private const string Source = """
        #include "lumyte.slang"
        struct FinalRootWire
        {
            uint Number;
            GpuRWBufferRef<uint> Output;
            GpuBufferRef<uint> Input;
            uint Padding;
        };
        ConstantBuffer<FinalRootWire> arguments;
        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main()
        {
            arguments.Output.Store(0, arguments.Number + arguments.Input.Load(0) + arguments.Padding);
        }
        """;

    /// <summary>Vulkan preserves independent roots, rejected selections, and explicit snapshots.</summary>
    /// <returns>The completed native readback checks in both validation modes.</returns>
    [GpuFact]
    public async Task VulkanFinalRootGenerationsAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.SpirV);
        foreach (bool validation in new[] { false, true })
        {
            using var device = VulkanDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await CheckAsync(device, artifact, validation);
        }
    }

    /// <summary>Wgpu preserves independent roots, rejected selections, and explicit snapshots.</summary>
    /// <returns>The completed native readback checks in both validation modes.</returns>
    [GpuFact]
    public async Task WgpuFinalRootGenerationsAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.Wgsl);
        foreach (bool validation in new[] { false, true })
        {
            using var device = WgpuDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await CheckAsync(device, artifact, validation);
        }
    }

    private static Task<ShaderArtifact> CompileAsync(ShaderTarget target)
        => new SlangShaderCompiler().CompileAsync(new() { Source = Source, Stage = ShaderStage.Compute, Target = target });

    private static async Task CheckAsync(IGraphicDevice device, ShaderArtifact artifact, bool validation)
    {
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsComputePipeline otherPipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsBuffer<uint> upload = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<uint> input = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead, Memory = MemoryPreference.Automatic });
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 3, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource, Memory = MemoryPreference.Automatic });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 3, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 2 });
        using IArgumentTable otherTable = device.CreateArgumentTable(new() { BufferCapacity = 2 });
        IGpuRef<uint> inputs = table.WriteBuffer(0, input.SliceElements(0, 2), ShaderBufferAccess.ReadOnly);
        IGpuRef<uint> outputs = table.WriteBuffer(1, output.SliceElements(0, 3), ShaderBufferAccess.ReadWrite);
        IGpuRef<uint> inputA = inputs.GetElement(0);
        IGpuRef<uint> inputB = inputs.GetElement(1);
        IGpuRef<uint> outputA = outputs.GetElement(0);
        IGpuRef<uint> outputB = outputs.GetElement(1);
        IGpuRef<uint> outputSnapshot = outputs.GetElement(2);
        var root = new Arguments(validation) { Number = 7, Input = inputA, Output = outputA };

        // Forty wire bytes require a 48-byte native root allocation, not a truncated uniform.
        Assert.Equal(40, Arguments.TransferSize);

        await upload.MapAsync();
        upload.CopyFrom(new uint[] { 100, 200 });
        upload.Unmap();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = upload.SliceElements(0, 2), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(upload.SliceElements(0, 2), input.SliceElements(0, 2));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = input.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        IComputeEncoder compute = commands.BeginComputePass(new());
        try
        {
            compute.SetPipeline(pipeline);
            compute.SetArgumentTable(table);

            // This is deliberately the first generic Set: a Snapshot writer throws in the codec.
            compute.SetArguments(in root);
            Assert.Equal(1, root.Writes);
            Assert.Equal(validation ? 1 : 0, root.Probes);
            root.Number = 9;
            root.Input = inputB;
            root.Output = outputB;
            compute.Dispatch(1);
            Assert.Equal(1, root.Writes);

            compute.SetArguments(in root);
            Assert.Equal(2, root.Writes);
            root.Number = 999;
            root.Input = inputA;
            root.Output = outputSnapshot;
            var failure = new InvalidOperationException("Codec failed after writing new final storage.");
            root.Failure = failure;
            Assert.Same(failure, Record.Exception(() => compute.SetArguments(in root)));
            root.Failure = null;
            Assert.Equal(3, root.Writes);
            Assert.Equal(validation ? 2 : 0, root.Probes);

            // Neither guard may consume the selected generation or commit its accesses.
            compute.SetPipeline(otherPipeline);
            Assert.Throws<InvalidOperationException>(() => compute.Dispatch(1));
            compute.SetPipeline(pipeline);
            compute.SetArgumentTable(otherTable);
            Assert.Throws<ArgumentException>(() => compute.Dispatch(1));
            compute.SetArgumentTable(table);
            Assert.Equal(3, root.Writes);

            // A rejecting metadata-only hook must not consume or replace the selected generation.
            var preparationFailure = new InvalidOperationException("Preparation rejected final metadata.");
            int prepares = 0;
            compute.SetArgumentValidation(metadata =>
            {
                prepares++;
                Assert.Equal(2, metadata.References.Count);
                throw preparationFailure;
            });
            try
            {
                Assert.Same(preparationFailure, Record.Exception(() => compute.SetArguments(in root)));
            }
            finally
            {
                compute.SetArgumentValidation(null);
            }

            Assert.Equal(1, prepares);
            compute.Dispatch(1);
            Assert.Equal(4, root.Writes);

            // A new typed selection owns numeric bytes independently of later source mutation.
            root.Number = 13;
            compute.SetArguments(in root);
            root.Number = 999;
            compute.Dispatch(1);
            Assert.Equal(5, root.Writes);
            Assert.Equal(validation ? 4 : 0, root.Probes);
        }
        finally
        {
            compute.End();
        }

        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.SliceElements(0, 3), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(output.SliceElements(0, 3), readback.SliceElements(0, 3));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, 3), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        try
        {
            uint[] actual = new uint[3];
            readback.CopyTo(actual);
            Assert.Equal(new uint[] { 107, 209, 113 }, actual);
        }
        finally
        {
            readback.Unmap();
        }
    }

    private sealed class Arguments(bool diagnostics) : IShaderArguments
    {
        public static string ShaderTypeName => "FinalRootWire";

        public static int TransferSize => Marshal.SizeOf<Wire>();

        public static IReadOnlyList<ShaderTransferField> TransferFields =>
        [
            new(nameof(Number), 0, 4, typeof(uint)),
            new(nameof(Output), 4, 16, typeof(ShaderReferenceSlot), true),
            new(nameof(Input), 20, 16, typeof(ShaderReferenceSlot), true),
            new(nameof(Wire.Padding), 36, 4, typeof(uint)),
        ];

        internal uint Number { get; set; }

        internal IGpuRef<uint>? Output { get; set; }

        internal IGpuRef<uint>? Input { get; set; }

        internal Exception? Failure { get; set; }

        internal int Writes { get; private set; }

        internal int Probes { get; private set; }

        public IReadOnlyList<ShaderTransferField> GetTransferFields()
        {
            Probes++;
            if (!diagnostics)
            {
                throw new InvalidOperationException("Disabled diagnostics must not probe the application object.");
            }

            return TransferFields;
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            if (typeof(TWriter).Name.Contains("Snapshot", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Generic Set must not capture an intermediate numeric snapshot.");
            }

            Writes++;
            var wire = new Wire { Number = Number, Output = default, Input = default, Padding = 0 };
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
            writer.WriteValue(bytes[..4]);
            if (Failure is { } failure)
            {
                throw failure;
            }

            writer.WriteReference(Output);
            writer.WriteReference(Input);
            writer.WriteValue(bytes[36..]);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct Wire
        {
            public uint Number;

            public ShaderReferenceSlot Output;

            public ShaderReferenceSlot Input;

            public uint Padding;
        }
    }
}
