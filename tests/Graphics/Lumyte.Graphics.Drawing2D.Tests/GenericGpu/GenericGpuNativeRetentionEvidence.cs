using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Test-only observations around the unchanged actual Sparse producer and native uploads.</summary>
public sealed partial class GenericGpuNativeTests
{
    private static string RetentionHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void RetentionEvidence(string name, byte[] bytes)
    {
        string? directory = Environment.GetEnvironmentVariable("LUMYTE_RETENTION_EVIDENCE");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name), bytes);
    }

    private static void AssertFreshNativeCopies(RetentionSlot warm, RetentionSlot old, RetentionSlot next, RetentionSlot replacement, object[] initialOwners, object[] editedOwners, RetentionInputs observations)
    {
        RetentionSlot[] slots = [warm, old, next, replacement];
        RetentionNativeCopy[] coverage = slots.Select(slot => Assert.Single(slot.NativeCopies, copy => copy.Bytes.Length == 4096 * sizeof(float))).ToArray();
        Assert.Equal(4, coverage.Select(copy => copy.Source).Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(4, coverage.Select(copy => copy.Destination).Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(4, coverage.Select(copy => copy.SourceNative).Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(4, coverage.Select(copy => copy.DestinationNative).Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.All(coverage, copy =>
        {
            Assert.DoesNotContain(copy.Source, editedOwners);
            Assert.DoesNotContain(copy.Destination, editedOwners);
            Assert.DoesNotContain(copy.SourceNative, editedOwners.Select(owner => ((IGraphicsNativeOrigin)owner).AllocationIdentity));
            Assert.DoesNotContain(copy.DestinationNative, editedOwners.Select(owner => ((IGraphicsNativeOrigin)owner).AllocationIdentity));
        });
        Assert.Equal(coverage[0].Bytes, coverage[1].Bytes);
        Assert.False(coverage[1].Bytes.SequenceEqual(coverage[2].Bytes));
        Assert.Equal(coverage[2].Bytes, coverage[3].Bytes);
        RetentionNativeCopy[] warmSource = warm.NativeCopies.Where(copy => initialOwners.Contains(copy.Destination)).ToArray();
        Assert.Equal(3, warmSource.Length);
        Assert.Contains(warmSource, copy => copy.Bytes.SequenceEqual(observations.Inputs[0].Scene[..256]));
        Assert.Contains(warmSource, copy => copy.Bytes.SequenceEqual(observations.Inputs[0].Scene[256..]));
        Assert.Contains(warmSource, copy => copy.Bytes.SequenceEqual(observations.Inputs[0].Frame));
        RetentionNativeCopy changedSource = Assert.Single(replacement.NativeCopies, copy => editedOwners.Contains(copy.Destination));
        Assert.Equal(observations.Inputs[3].Scene[256..], changedSource.Bytes);
        Assert.DoesNotContain(changedSource.Destination, initialOwners);
        string run = Guid.NewGuid().ToString("N");
        var ledger = new
        {
            Inputs = observations.Inputs.Select((input, index) => new { index, sceneHash = RetentionHash(input.Scene), frameHash = RetentionHash(input.Frame), metadata = input.Extent.Metadata }),
            Copies = slots.Select((slot, index) => new { index, copies = slot.NativeCopies.Select((copy, j) => new { j, bytes = copy.Bytes.Length, hash = RetentionHash(copy.Bytes), source = RuntimeHelpers.GetHashCode(copy.Source), destination = RuntimeHelpers.GetHashCode(copy.Destination), sourceNative = RuntimeHelpers.GetHashCode(copy.SourceNative), destinationNative = RuntimeHelpers.GetHashCode(copy.DestinationNative), cached = editedOwners.Contains(copy.Destination) }) }),
        };
        RetentionEvidence($"retention-{run}.json", JsonSerializer.SerializeToUtf8Bytes(ledger, new JsonSerializerOptions { WriteIndented = true }));
        for (int i = 0; i < slots.Length; i++)
        {
            for (int j = 0; j < slots[i].NativeCopies.Count; j++)
            {
                RetentionEvidence($"native-{run}-{i}-{j}.bin", slots[i].NativeCopies[j].Bytes);
            }
        }
    }

    private sealed record RetentionInput(byte[] Scene, byte[] Frame, SchemaExtent Extent);

    private sealed class RetentionInputs : IStage<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>>, IStageFactory<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>>
    {
        private readonly CpuSparseCoverage2D _actual = new(new(16, 1), 256L * 1024 * 1024);

        public IStageFactory<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>> Factory => this;

        internal List<RetentionInput> Inputs { get; } = [];

        public StageRequirements Describe(StageInputDescriptions inputs) => _actual.Factory.Describe(inputs);

        public IStageProcessor<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>> Create(StageBuildContext context) => new Observer(this, _actual.Factory.Create(context));

        internal void AssertVersions()
        {
            Assert.Equal(4, Inputs.Count);
            Assert.All(Inputs, input => Assert.Equal(496, input.Scene.Length));
            Assert.Equal(Inputs[0].Scene, Inputs[1].Scene);
            Assert.Equal(Inputs[0].Scene, Inputs[2].Scene);
            Assert.Equal(Inputs[0].Scene[..256], Inputs[3].Scene[..256]);
            Assert.False(Inputs[0].Scene[256..].SequenceEqual(Inputs[3].Scene[256..]));
            Assert.All(Inputs, input => Assert.Equal(Inputs[0].Frame, input.Frame));
            Assert.Equal(4, Inputs.Select(input => input.Extent.Metadata["source"]).Distinct().Count());
            for (int i = 0; i < Inputs.Count; i++)
            {
                RetentionInput input = Inputs[i];
                Assert.Equal(i < 2 ? 0f : 3f, Convert.ToSingle(input.Extent.Metadata["translationX"]));
                Assert.Equal(i < 2 ? 0f : 1f, Convert.ToSingle(input.Extent.Metadata["translationY"]));
                Assert.Equal(16, Convert.ToInt32(input.Extent.Metadata["width"]));
                Assert.Equal(16, Convert.ToInt32(input.Extent.Metadata["height"]));
                Assert.Equal(31, input.Extent.ElementCount);
            }
        }

        private sealed class Observer(RetentionInputs owner, IStageProcessor<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>> actual) : IStageProcessor<Host<SceneV1>, Host<FrameV1>, Host<Coverage16V1>>
        {
            public ValueTask<ValueLease<Host<Coverage16V1>>> PrepareAsync(StagePreparationContext context, ValueLease<Host<SceneV1>> a, ValueLease<Host<FrameV1>> b, CancellationToken cancellationToken)
            {
                SchemaData<SceneV1> scene = context.ReadHost(a);
                SchemaData<FrameV1> frame = context.ReadHost(b);
                owner.Inputs.Add(new(MemoryMarshal.AsBytes(scene.ReadElements<Vector4>()).ToArray(), MemoryMarshal.AsBytes(frame.ReadElements<Vector4>()).ToArray(), scene.Extent));
                return actual.PrepareAsync(context, a, b, cancellationToken);
            }

            public void Dispose() => actual.Dispose();
        }
    }

    private sealed class RetentionNativeCopy(object source, object destination, IGraphicsBuffer<byte> readback) : IDisposable
    {
        internal object Source { get; } = source;

        internal object Destination { get; } = destination;

        internal object SourceNative { get; } = ((IGraphicsNativeOrigin)source).AllocationIdentity;

        internal object DestinationNative { get; } = ((IGraphicsNativeOrigin)destination).AllocationIdentity;

        internal byte[] Bytes { get; private set; } = [];

        public void Dispose() => readback.Dispose();

        internal static RetentionNativeCopy Capture<T>(IGraphicsCommandBuffer commands, BufferSlice<T> source, object destination)
            where T : unmanaged
        {
            IGraphicDevice device = ((IGraphicsNativeOrigin)commands).Device;
            IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = source.SizeInBytes, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            commands.CopyBuffer(source, readback.SliceElements(0, readback.Count));
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            return new(source.Buffer, destination, readback);
        }

        internal void Complete(ExecutionRuntime runtime)
        {
            Pump(runtime, readback.MapAsync().AsTask());
            Bytes = new byte[readback.Count];
            readback.CopyTo(Bytes);
            readback.Unmap();
        }
    }
}
