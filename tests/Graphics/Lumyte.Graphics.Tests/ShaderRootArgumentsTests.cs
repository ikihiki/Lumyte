using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks final-storage root ownership without a native device or allocation budgets.</summary>
public sealed class ShaderRootArgumentsTests
{
    /// <summary>Each selection writes once straight into independently owned final storage.</summary>
    [Fact]
    public void CreateStreamsOnceAndOwnsIndependentGenerations()
    {
        var factory = new StorageFactory();
        var value = new Arguments { Value = 17 };
        using var first = ShaderRootArguments.Create(in value, factory.Create);
        value.Value = 29;
        using var second = ShaderRootArguments.Create(in value, factory.Create);
        value.Value = 91;

        Assert.Equal(2, value.Writes);
        Assert.Equal(2, factory.Calls);
        Assert.NotSame(first.Storage, second.Storage);
        Assert.NotSame(factory.Items[0].Bytes, factory.Items[1].Bytes);
        Assert.Equal(17u, Read(factory.Items[0]));
        Assert.Equal(29u, Read(factory.Items[1]));
        Assert.All(factory.Items, storage => Assert.Equal(1, storage.Writes));
        Assert.Equal(0, value.Probes);
        AssertNoRetainedInput(first, value);
        AssertNoRetainedInput(second, value);
    }

    /// <summary>Unmanaged input is copied synchronously, with only trailing capacity alignment.</summary>
    [Fact]
    public void UnmanagedCreateCopiesIntoFinalStorage()
    {
        var factory = new StorageFactory();
        uint value = 42;
        using var root = ShaderRootArguments.CreateUnmanaged(in value, "arguments", "Wire", factory.Create);
        value = 99;
        Storage storage = Assert.Single(factory.Items);
        Assert.Equal(4, root.WireSize);
        Assert.Equal(16, storage.Size);
        Assert.Equal(42u, Read(storage));
        Assert.Equal(1, storage.Writes);
        Assert.Empty(root.References.Values);
    }

    /// <summary>Selection prepares identities; execution admission and reference patching occur later.</summary>
    [Fact]
    public void ScopePreparesBeforeAdmissionAndSealPatchesOnlyOnce()
    {
        using var table = new Table();
        var reference = new Reference(table, new object());
        var scope = new Scope();
        var factory = new StorageFactory();
        var value = new Arguments { Value = 7, Reference = reference };
        using var root = ShaderRootArguments.Create(in value, factory.Create, scope: scope);
        Storage storage = Assert.Single(factory.Items);
        Assert.Same(root.References, Assert.Single(scope.Prepared));
        Assert.Same(reference, Assert.Single(root.References.Values).Reference);
        Assert.Empty(scope.Admitted);
        Assert.Equal(0, storage.Seals);
        int encodes = 0;
        root.Seal(member =>
        {
            Assert.Same(reference, member.Reference);
            encodes++;
            return Enumerable.Repeat((byte)0x5a, 16).ToArray();
        });
        byte[] sealedBytes = storage.Bytes.ToArray();
        root.Seal(_ => throw new InvalidOperationException("Already sealed."));
        root.Admit([reference]);
        Assert.Same(reference, Assert.Single(Assert.Single(scope.Admitted)));
        Assert.Equal(1, encodes);
        Assert.Equal(2, storage.Writes);
        Assert.Equal(1, storage.Seals);
        Assert.Equal(sealedBytes, storage.Bytes);
        Assert.Equal(7u, Read(storage));
        Assert.All(storage.Bytes[16..32], item => Assert.Equal((byte)0x5a, item));
    }

    /// <summary>Explicit compatibility input copies bytes and fixup identities, not its diagnostic provider.</summary>
    [Fact]
    public void ExplicitSnapshotIsDefensivelyCopiedWithoutRetainingProvider()
    {
        using var table = new Table();
        var reference = new Reference(table, new object());
        byte[] bytes = new byte[32];
        bytes[0] = 23;
        ShaderValue[] values = [new(typeof(object), reference, 16)];
        var provider = new Arguments();
        var snapshot = new ShaderValueSnapshot("arguments", "Wire", bytes, values, []) { DiagnosticSource = provider };
        var factory = new StorageFactory();
        using var root = ShaderRootArguments.CreateSnapshot(snapshot, factory.Create);
        bytes[0] = 91;
        values[0] = new(typeof(object), null, 0);
        provider.Value = 100;
        Assert.Equal(23u, Read(Assert.Single(factory.Items)));
        Assert.Same(reference, Assert.Single(root.References.Values).Reference);
        Assert.Equal(16, Assert.Single(root.References.Values).Offset);
        Assert.Equal(0, provider.Probes);
        Assert.Equal(0, provider.Writes);
        AssertNoRetainedInput(root, provider);
        AssertNoRetainedInput(root, snapshot);
        var binding = ShaderBindingSnapshot.Capture(root, table);
        Assert.Same(root, binding.Arguments);
        Assert.Throws<InvalidOperationException>(() => binding.Root);
        Assert.Equal("arguments", binding.RootParameter);
        Assert.Equal(1, factory.Items[0].Writes);
    }

    /// <summary>The old explicit snapshot capture retains its public root compatibility property.</summary>
    [Fact]
    public void LegacyCaptureStillExposesSnapshotRoot()
    {
        var snapshot = new ShaderValueSnapshot("arguments", "Wire", new byte[4], [], []);
        var binding = ShaderBindingSnapshot.Capture(snapshot, null);
        Assert.Same(snapshot, binding.Root);
        Assert.Null(binding.Arguments);
    }

    /// <summary>Codec and scope failures dispose only the failed generation and preserve the original error.</summary>
    /// <param name="failScope">Whether scope preparation, rather than codec writing, fails.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSelectionPreservesPreviousRootAndOriginalException(bool failScope)
    {
        var factory = new StorageFactory();
        var value = new Arguments { Value = 11 };
        using var previous = ShaderRootArguments.Create(in value, factory.Create);
        var failure = new InvalidOperationException("Selection failed.");
        factory.ThrowOnDispose = true;
        var scope = new Scope { Failure = failScope ? failure : null };
        value.Failure = failScope ? null : failure;
        Exception? actual = Record.Exception(() => ShaderRootArguments.Create(in value, factory.Create, scope: scope));
        Assert.Same(failure, actual);
        Assert.Equal(1, factory.Items[1].Disposes);
        Assert.Equal(0, factory.Items[0].Disposes);
        Assert.Equal(11u, Read(factory.Items[0]));
    }

    /// <summary>Factory exceptions are propagated without changing a previously successful generation.</summary>
    [Fact]
    public void FactoryFailurePreservesPreviousRoot()
    {
        var factory = new StorageFactory();
        var value = new Arguments { Value = 13 };
        using var previous = ShaderRootArguments.Create(in value, factory.Create);
        var failure = new InvalidOperationException("Allocation failed.");
        Assert.Same(failure, Record.Exception(() => ShaderRootArguments.Create(in value, _ => throw failure)));
        Assert.Equal(1, factory.Calls);
        Assert.Equal(13u, Read(factory.Items[0]));
        Assert.Equal(0, factory.Items[0].Disposes);
    }

    /// <summary>Wrong-sized final allocation is rejected and cleaned up even if cleanup throws.</summary>
    /// <param name="size">The invalid physical storage capacity returned by the factory.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(48)]
    public void WrongNativeCapacityIsRejectedAndDisposed(int size)
    {
        var value = new Arguments();
        var storage = new Storage(size) { ThrowOnDispose = true };
        Assert.Throws<InvalidOperationException>(() => ShaderRootArguments.Create(in value, _ => storage));
        Assert.Equal(1, storage.Disposes);
        Assert.Equal(0, value.Writes);
    }

    /// <summary>Mandatory bounds and bulk-write invariants do not depend on optional diagnostics.</summary>
    /// <param name="mode">The codec invariant violation to exercise.</param>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("outside")]
    [InlineData("wrong-size")]
    [InlineData("twice")]
    [InlineData("missing")]
    public void InvalidCodecIsRejectedAndStorageDisposed(string mode)
    {
        var factory = new StorageFactory();
        var value = new Arguments { Mode = mode };
        if (mode is "duplicate" or "negative" or "outside")
        {
            Assert.Throws<ArgumentException>(() => ShaderRootArguments.Create(in value, factory.Create));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => ShaderRootArguments.Create(in value, factory.Create));
        }

        Assert.Equal(1, Assert.Single(factory.Items).Disposes);
        Assert.Equal(0, value.Probes);
    }

    /// <summary>Reference encoders must produce exactly the common ABI length before any patch is written.</summary>
    /// <param name="length">The invalid encoded reference length in bytes.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void SealRejectsWrongReferenceAbiLength(int length)
    {
        using var table = new Table();
        var value = new Arguments { Reference = new Reference(table, new object()) };
        var factory = new StorageFactory();
        using var root = ShaderRootArguments.Create(in value, factory.Create);
        Assert.Throws<ArgumentException>(() => root.Seal(_ => new byte[length]));
        Assert.Equal(1, factory.Items[0].Writes);
        Assert.Equal(0, factory.Items[0].Seals);
    }

    /// <summary>Diagnostics probe class providers explicitly and derive struct metadata without interface probes.</summary>
    [Fact]
    public void EnabledDiagnosticsValidateClassAndStructMetadata()
    {
        ShaderDataLayout layout = DiagnosticLayout();
        var factory = new StorageFactory();
        var value = new NumericArguments { Value = 19 };
        using var classRoot = ShaderRootArguments.Create(in value, factory.Create, layout);
        Assert.Equal(1, value.Probes);
        Assert.Equal(19u, Read(factory.Items[0]));
        var numeric = new NumericStruct { Value = 31 };
        using var structRoot = ShaderRootArguments.Create(in numeric, factory.Create, layout);
        Assert.Equal(31u, Read(factory.Items[1]));
    }

    /// <summary>A failing diagnostic provider preserves its original exception and cleans up final storage.</summary>
    [Fact]
    public void DiagnosticFailureDisposesStorageAndPreservesException()
    {
        var failure = new InvalidOperationException("Metadata failed.");
        var value = new NumericArguments { Failure = failure };
        var factory = new StorageFactory { ThrowOnDispose = true };
        Assert.Same(failure, Record.Exception(() => ShaderRootArguments.Create(in value, factory.Create, DiagnosticLayout())));
        Assert.Equal(1, Assert.Single(factory.Items).Disposes);
        Assert.Equal(1, value.Probes);
    }

    /// <summary>Each execution refreshes transitive metadata while the selected root identities stay stable.</summary>
    [Fact]
    public void BindingCaptureRefreshesTransitiveMetadataWithoutRewritingRoot()
    {
        using var table = new Table();
        var source = new DataSource();
        var rootReference = new Reference(table, source);
        var firstReference = new Reference(table, new object());
        var secondReference = new Reference(table, new object());
        var value = new Arguments { Reference = rootReference };
        var factory = new StorageFactory();
        var scope = new Scope();
        using var root = ShaderRootArguments.Create(in value, factory.Create, scope: scope);
        ShaderReferenceMetadata current = new([new(typeof(object), firstReference, 0)]);
        int reads = 0;
        ShaderReferenceMetadata ReadMetadata(IShaderDataSource buffer, ulong index)
        {
            Assert.Same(source, buffer);
            Assert.Equal(0ul, index);
            reads++;
            return current;
        }

        var first = ShaderBindingSnapshot.Capture(root, table, ReadMetadata);
        current = new([new(typeof(object), secondReference, 0)]);
        var second = ShaderBindingSnapshot.Capture(root, table, ReadMetadata);
        Assert.Contains(firstReference, first.References);
        Assert.DoesNotContain(secondReference, first.References);
        Assert.Contains(secondReference, second.References);
        Assert.DoesNotContain(firstReference, second.References);
        Assert.Same(rootReference, Assert.Single(root.References.Values).Reference);
        Assert.NotSame(first.Elements[(source, 0)], second.Elements[(source, 0)]);
        Assert.Equal(2, reads);
        Assert.Equal(2, scope.Admitted.Count);
        Assert.Equal(1, value.Writes);
        Assert.Equal(1, factory.Items[0].Writes);
        Assert.Equal(0, source.Reads);
        Assert.Contains(firstReference, scope.Admitted[0]);
        Assert.Contains(secondReference, scope.Admitted[1]);
    }

    /// <summary>Root disposal is idempotent and prevents subsequent execution.</summary>
    [Fact]
    public void DisposeReleasesFinalStorageOnce()
    {
        var value = new Arguments();
        var factory = new StorageFactory();
        var root = ShaderRootArguments.Create(in value, factory.Create);
        root.Dispose();
        root.Dispose();
        Assert.Equal(1, factory.Items[0].Disposes);
        Assert.Throws<ObjectDisposedException>(() => root.Seal(_ => new byte[16]));
        Assert.Throws<ObjectDisposedException>(() => root.Admit([]));
    }

    private static uint Read(Storage storage) => MemoryMarshal.Read<uint>(storage.Bytes);

    private static void AssertNoRetainedInput(ShaderRootArguments root, object input)
    {
        // Ownership identity checks, not GC timing, byte budgets or readonly type-shape checks.
        foreach (FieldInfo field in typeof(ShaderRootArguments).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            object? retained = field.GetValue(root);
            Assert.NotSame(input, retained);
            Assert.False(retained is ShaderValueSnapshot);
        }
    }

    private static ShaderDataLayout DiagnosticLayout()
    {
        const string Reflection = """
            {"parameters":[{"name":"arguments","type":{"kind":"constantBuffer","elementType":{
              "kind":"struct","name":"Wire","sizes":[{"kind":"uniform","value":4}],
              "fields":[{"name":"Value","type":{"kind":"scalar","scalarType":"uint32"},"binding":{"offset":0,"size":4}}]
            }}}]}
            """;
        var target = new ShaderTargetData(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", "row-major", [], Reflection);
        return ShaderDataLayout.Root(target, "arguments");
    }

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private struct Wire
    {
        public uint Value;
    }

    private struct NumericStruct : IShaderArguments
    {
        public uint Value;

        public static string ShaderTypeName => "Wire";

        public static int TransferSize => 4;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("Use type-derived fields.");

        public IReadOnlyList<ShaderTransferField> GetTransferFields() => throw new InvalidOperationException("Do not box diagnostic providers.");

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => writer.WriteBytes(in this);
    }

    private sealed class Arguments : IShaderArguments
    {
        public static string ShaderTypeName => "Wire";

        public static int TransferSize => 32;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("Diagnostics disabled.");

        internal uint Value { get; set; }

        internal IGpuRef<object>? Reference { get; set; }

        internal string? Mode { get; set; }

        internal Exception? Failure { get; set; }

        internal int Writes { get; private set; }

        internal int Probes { get; private set; }

        public IReadOnlyList<ShaderTransferField> GetTransferFields()
        {
            Probes++;
            throw new InvalidOperationException("Diagnostics disabled.");
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            if (typeof(TWriter).Name.Contains("Snapshot", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Root selection must not capture a numeric snapshot first.");
            }

            Writes++;
            if (Failure is { } failure)
            {
                throw failure;
            }

            if (Mode == "missing")
            {
                return;
            }

            if (Mode == "wrong-size")
            {
                uint small = Value;
                writer.WriteBytes(in small);
                return;
            }

            var wire = new Wire { Value = Value };
            writer.WriteBytes(in wire);
            if (Mode == "twice")
            {
                writer.WriteBytes(in wire);
            }

            if (Reference != null || Mode is "duplicate" or "negative" or "outside")
            {
                writer.WriteReference(Mode == "negative" ? -1 : Mode == "outside" ? 17 : 16, Reference);
                if (Mode == "duplicate")
                {
                    writer.WriteReference(16, Reference);
                }
            }
        }
    }

    private sealed class NumericArguments : IShaderArguments
    {
        public static string ShaderTypeName => "Wire";

        public static int TransferSize => 4;

        internal uint Value { get; set; }

        internal int Probes { get; private set; }

        internal Exception? Failure { get; init; }

        public IReadOnlyList<ShaderTransferField> GetTransferFields()
        {
            Probes++;
            if (Failure is { } failure)
            {
                throw failure;
            }

            return [new("Value", 0, 4, typeof(uint))];
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            uint value = Value;
            writer.WriteBytes(in value);
        }
    }

    // This byte array IS the test's final native allocation, not an intermediate snapshot.
    private sealed class Storage(int size) : IShaderRootStorage
    {
        private bool _sealed;

        public int Size => Bytes.Length;

        internal byte[] Bytes { get; } = new byte[size];

        internal int Writes { get; private set; }

        internal int Seals { get; private set; }

        internal int Disposes { get; private set; }

        internal bool ThrowOnDispose { get; init; }

        public void Write(ReadOnlySpan<byte> bytes, int offset)
        {
            if (_sealed)
            {
                throw new InvalidOperationException("Final storage is immutable after seal.");
            }

            bytes.CopyTo(Bytes.AsSpan(offset, bytes.Length));
            Writes++;
        }

        public void Seal()
        {
            Seals++;
            _sealed = true;
        }

        public void Dispose()
        {
            Disposes++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("Cleanup failed.");
            }
        }
    }

    private sealed class StorageFactory
    {
        internal List<Storage> Items { get; } = [];

        internal int Calls { get; private set; }

        internal bool ThrowOnDispose { get; set; }

        internal IShaderRootStorage Create(int size)
        {
            Calls++;
            var storage = new Storage(size) { ThrowOnDispose = ThrowOnDispose };
            Items.Add(storage);
            return storage;
        }
    }

    private sealed class Scope : IShaderArgumentScope
    {
        internal List<ShaderReferenceMetadata> Prepared { get; } = [];

        internal List<IReadOnlyCollection<IShaderReference>> Admitted { get; } = [];

        internal Exception? Failure { get; init; }

        public void Prepare(ShaderReferenceMetadata references)
        {
            if (Failure is { } failure)
            {
                throw failure;
            }

            Prepared.Add(references);
        }

        public void Admit(IReadOnlyCollection<IShaderReference> references) => Admitted.Add(references);
    }

    private sealed class Reference(IArgumentTable table, object resource) : IGpuRef<object>, IShaderReference
    {
        public object Table => table;

        public object Resource => resource;

        public ShaderBufferAccess BufferAccess => ShaderBufferAccess.ReadOnly;

        public ulong RegistrationOffsetInBytes => 0;

        public ulong RegistrationSizeInBytes => SizeInBytes;

        public uint Slot => 0;

        public ulong OffsetInBytes => 0;

        public ulong SizeInBytes => 16;

        public ulong Count => 1;

        public IGpuRef<object> GetElement(ulong index) => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));
    }

    private sealed class DataSource : IShaderDataSource
    {
        public IShaderDataLayout Layout { get; } = new ElementLayout();

        public ulong SizeInBytes => 16;

        public MemoryPreference Memory => MemoryPreference.Automatic;

        public object ShaderHandle => this;

        internal int Reads { get; private set; }

        public ShaderReferenceMetadata ReadReferences(ulong index)
        {
            Reads++;
            throw new InvalidOperationException("Expected command-local metadata reader.");
        }

        public void SetTransferredReferences(ulong index, ShaderReferenceMetadata value) => throw new NotSupportedException();

        public void ValidateAlive()
        {
        }
    }

    private sealed class ElementLayout : IShaderDataLayout
    {
        public string Name => "Wire";

        public int Size => 16;

        public bool Matches(IShaderDataLayout layout) => ReferenceEquals(this, layout);

        public byte[] Pack(ShaderValueSnapshot snapshot, Func<ShaderValue, byte[]> reference) => throw new NotSupportedException();
    }

    private sealed class Table : IArgumentTable
    {
        public uint TextureCapacity => 1;

        public uint SamplerCapacity => 1;

        public uint BufferCapacity => 1;

        public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view) => throw new NotSupportedException();

        public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => throw new NotSupportedException();

        public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
            where T : unmanaged => throw new NotSupportedException();

        public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
            where T : IShaderData => throw new NotSupportedException();

        public void ReleaseTexture(uint slot) => throw new NotSupportedException();

        public void ReleaseSampler(uint slot) => throw new NotSupportedException();

        public void ReleaseBuffer(uint slot) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
