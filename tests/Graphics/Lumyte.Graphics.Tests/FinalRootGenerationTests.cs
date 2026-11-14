using System.Reflection;
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
        var root = new Arguments() { Number = 7, Input = inputA, Output = outputA };

        // Forty wire bytes require a 48-byte native root allocation, not a truncated uniform.
        Assert.Equal(40, Arguments.TransferSize);

        await upload.MapAsync();
        upload.CopyFrom(new uint[] { 100, 200 });
        upload.Unmap();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = upload.SliceElements(0, 2), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(upload.SliceElements(0, 2), input.SliceElements(0, 2));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = input.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        var nativeOwners = new List<object>();
        IComputeEncoder compute = commands.BeginComputePass(new());
        try
        {
            compute.SetPipeline(pipeline);
            compute.SetArgumentTable(table);

            // This is deliberately the first generic Set: a Snapshot writer throws in the codec.
            compute.SetArguments(in root);
            CaptureWgpuOwner(device, compute, nativeOwners);
            if (device is VulkanDevice)
            {
                var selected = (Lumyte.Graphics.Shared.ShaderRootArguments)compute.GetType().GetField("_arguments", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(compute)!;
                Assert.Null(selected.GetType().GetProperty("Storage"));
                if (device.ValidationEnabled)
                {
                    Assert.Equal(2, Assert.IsAssignableFrom<IShaderReferenceMetadata>(selected.References).References.Count);
                }
                else
                {
                    Assert.Null(selected.References);
                }
            }

            Assert.Equal(1, root.Writes);
            Assert.Equal(0, root.Probes);
            root.Number = 9;
            root.Input = inputB;
            root.Output = outputB;
            compute.Dispatch(1);
            Assert.Equal(1, root.Writes);

            compute.SetArguments(in root);
            CaptureWgpuOwner(device, compute, nativeOwners);
            Assert.Equal(2, root.Writes);
            root.Number = 999;
            root.Input = inputA;
            root.Output = outputSnapshot;
            var failure = new InvalidOperationException("Codec failed after writing new final storage.");
            root.Failure = failure;
            Assert.Same(failure, Record.Exception(() => compute.SetArguments(in root)));
            root.Failure = null;
            Assert.Equal(3, root.Writes);
            Assert.Equal(0, root.Probes);

            // All malformed codecs must leave the previous valid generation selected.
            int writesBeforeMalformed = root.Writes;
            int probesBeforeMalformed = root.Probes;
            foreach (int mode in new[] { 1, 2, 3 })
            {
                root.BadCodec = mode;
                if (mode == 3)
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => compute.SetArguments(in root));
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => compute.SetArguments(in root));
                }
            }

            root.BadCodec = 0;
            Assert.Equal(writesBeforeMalformed + 3, root.Writes);
            Assert.Equal(probesBeforeMalformed, root.Probes);

            // Neither guard may consume the selected generation or commit its accesses.
            compute.SetPipeline(otherPipeline);
            Assert.Throws<InvalidOperationException>(() => compute.Dispatch(1));
            compute.SetPipeline(pipeline);
            compute.SetArgumentTable(otherTable);
            Assert.Throws<ArgumentException>(() => compute.Dispatch(1));
            compute.SetArgumentTable(table);
            Assert.Equal(6, root.Writes);

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
            Assert.Equal(7, root.Writes);

            // Transition tables before selecting their matching root; stale roots still reject execution.
            IGpuRef<uint> otherInput = otherTable.WriteBuffer(0, input.SliceElements(0, 1), ShaderBufferAccess.ReadOnly);
            IGpuRef<uint> otherOutput = otherTable.WriteBuffer(1, output.SliceElements(0, 3), ShaderBufferAccess.ReadWrite).GetElement(2);
            compute.SetArgumentTable(otherTable);
            Assert.Throws<ArgumentException>(() => compute.Dispatch(1));
            Assert.Throws<ArgumentException>(() => compute.SetArguments(in root));
            root.Input = otherInput;
            root.Output = otherOutput;

            // A new typed selection owns numeric bytes independently of later source mutation.
            root.Number = 13;
            compute.SetArguments(in root);
            CaptureWgpuOwner(device, compute, nativeOwners);
            compute.SetArgumentTable(table);
            Assert.Throws<ArgumentException>(() => compute.Dispatch(1));
            root.Input = inputA;
            root.Output = outputSnapshot;
            compute.SetArguments(in root);
            CaptureWgpuOwner(device, compute, nativeOwners);
            root.Number = 999;
            compute.Dispatch(1);
            Assert.Equal(10, root.Writes);
            Assert.Equal(0, root.Probes);
        }
        finally
        {
            compute.End();
        }

        Assert.All(nativeOwners, owner => Assert.False(IsDisposed(owner)));
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

        commands.Dispose();
        Assert.All(nativeOwners, owner => Assert.True(IsDisposed(owner)));
    }

    private static void CaptureWgpuOwner(IGraphicDevice device, IComputeEncoder encoder, List<object> owners)
    {
        if (device is WgpuDevice)
        {
            object owner = encoder.GetType().GetField("_arguments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(encoder)!;
            Assert.Equal("Lumyte.Graphics.Wgpu.WgpuRootStorage", owner.GetType().FullName);
            Assert.IsAssignableFrom<IDisposable>(owner);
            Assert.False(IsDisposed(owner));
            Assert.DoesNotContain(owners, previous => ReferenceEquals(previous, owner));
            owners.Add(owner);
        }
    }

    private static bool IsDisposed(object owner)
        => (bool)owner.GetType().GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private struct Arguments() : IShaderArguments
    {
        private readonly System.Runtime.CompilerServices.StrongBox<int> _counter = new();

        public static string ShaderTypeName => "FinalRootWire";

        public static int TransferSize => Marshal.SizeOf<Wire>();

        public static int ReferenceCount => 2;

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

        internal int BadCodec { get; set; }

        internal Exception? Failure { get; set; }

        internal int Writes => _counter.Value;

        internal int Probes { get; private set; }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            if (typeof(TWriter).Name.Contains("Snapshot", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Generic Set must not capture an intermediate numeric snapshot.");
            }

            _counter.Value++;
            if (BadCodec != 0)
            {
                if (BadCodec != 1)
                {
                    writer.WriteValue(new byte[BadCodec == 2 ? 4 : 89]);
                }

                return;
            }

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
