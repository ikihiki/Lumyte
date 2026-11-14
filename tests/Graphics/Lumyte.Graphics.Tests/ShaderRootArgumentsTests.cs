using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks final-storage root ownership without a native device or allocation budgets.</summary>
public sealed class ShaderRootArgumentsTests
{
    /// <summary>Minimal metadata owns an immutable ordered identity copy without legacy wire locations.</summary>
    [Fact]
    public void DependencyMetadataCopiesOrderedIdentities()
    {
        using var table = new Table();
        var first = new Reference(table, new object());
        var second = new Reference(table, new object());
        IShaderReference[] source = [first, second, first];
        var metadata = new ShaderDependencyMetadata(source);
        source[0] = second;
        Assert.Equal(new IShaderReference[] { first, second, first }, metadata.References);
        IList<IShaderReference> references = Assert.IsAssignableFrom<IList<IShaderReference>>(metadata.References);
        Assert.True(references.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => references[0] = second);
        Assert.Equal(nameof(IShaderReferenceMetadata.References), Assert.Single(typeof(ShaderDependencyMetadata).GetProperties()).Name);
    }

    /// <summary>Legacy offset-list metadata and its layout-validation entry point are absent.</summary>
    [Fact]
    public void LegacyReferenceMetadataApiIsAbsent()
    {
        Assert.Null(typeof(IShaderReferenceMetadata).Assembly.GetType("Lumyte.Graphics.Abstractions.ShaderReferenceMetadata"));
        Assert.Null(typeof(ShaderDataLayout).GetMethod("ValidateReferenceMetadata"));
    }

    /// <summary>Prepared storage transfers ownership only after successful root construction.</summary>
    [Fact]
    public void PreparedStorageOwnershipTransfersOnSuccessOnly()
    {
        var storage = new Storage(32);
        var metadata = new ShaderDependencyMetadata([]);
        var failure = new InvalidOperationException("Preparation failed.");
        Assert.Same(storage, ShaderRootArguments.Allocate("arguments", "Wire", 20, capacity =>
        {
            Assert.Equal(32, capacity);
            return storage;
        }));
        Assert.Same(failure, Record.Exception(() => ShaderRootArguments.Create("arguments", "Wire", 20, storage, metadata, _ => throw failure)));
        Assert.Equal(0, storage.Disposes);
        using (var root = ShaderRootArguments.Create("arguments", "Wire", 20, storage, metadata))
        {
            Assert.Same(storage, root.Storage);
            Assert.Same(metadata, root.References);
            Assert.Equal(0, storage.Writes);
        }

        Assert.Equal(1, storage.Disposes);
    }

    /// <summary>Each selection writes once straight into independently owned final storage.</summary>
    [Fact]
    public void CreateStreamsOnceAndOwnsIndependentGenerations()
    {
        var factory = new StorageFactory();
        var value = new Arguments { Value = 17 };
        using ShaderRootArguments first = TestShaderTransfer.Create(in value, factory.Create, Encode);
        value.Value = 29;
        using ShaderRootArguments second = TestShaderTransfer.Create(in value, factory.Create, Encode);
        value.Value = 91;

        Assert.Equal(2, value.Writes);
        Assert.Equal(2, factory.Calls);
        Assert.NotSame(first.Storage, second.Storage);
        Assert.NotSame(factory.Items[0].Bytes, factory.Items[1].Bytes);
        Assert.Equal(17u, Read(factory.Items[0]));
        Assert.Equal(29u, Read(factory.Items[1]));
        Assert.All(factory.Items, storage => Assert.Equal(2, storage.Writes));
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
        using ShaderRootArguments root = TestShaderTransfer.CreateUnmanaged(in value, "arguments", "Wire", factory.Create);
        value = 99;
        Storage storage = Assert.Single(factory.Items);
        Assert.Equal(4, root.WireSize);
        Assert.Equal(16, storage.Size);
        Assert.Equal(42u, Read(storage));
        Assert.Equal(1, storage.Writes);
        Assert.Empty(root.References.References);
    }

    /// <summary>Selection encodes references and prepares identities; execution admits and seals only once.</summary>
    [Fact]
    public void PrepareRunsBeforeAdmissionAndSealDoesNotRewrite()
    {
        using var table = new Table();
        var reference = new Reference(table, new object());
        var prepared = new List<IShaderReferenceMetadata>();
        var admitted = new List<IReadOnlyCollection<IShaderReference>>();
        Action<IReadOnlyCollection<IShaderReference>>? Prepare(IShaderReferenceMetadata references)
        {
            prepared.Add(references);
            return admitted.Add;
        }

        var factory = new StorageFactory();
        var value = new Arguments { Value = 7, Reference = reference };
        using ShaderRootArguments root = TestShaderTransfer.Create(in value, factory.Create, Encode, prepare: Prepare);
        Storage storage = Assert.Single(factory.Items);
        Assert.Same(root.References, Assert.Single(prepared));
        Assert.Same(reference, Assert.Single(root.References.References));
        Assert.Empty(admitted);
        Assert.Equal(0, storage.Seals);
        byte[] sealedBytes = storage.Bytes.ToArray();
        root.Seal();
        root.Seal();
        root.Admit([reference]);
        Assert.Same(reference, Assert.Single(Assert.Single(admitted)));
        Assert.Equal(2, storage.Writes);
        Assert.Equal(1, storage.Seals);
        Assert.Equal(sealedBytes, storage.Bytes);
        Assert.Equal(7u, Read(storage));
        Assert.All(storage.Bytes[16..32], item => Assert.Equal((byte)0x5a, item));
    }

    /// <summary>Codec and preparation failures dispose only the failed generation and preserve the original error.</summary>
    /// <param name="failPrepare">Whether preparation, rather than codec writing, fails.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSelectionPreservesPreviousRootAndOriginalException(bool failPrepare)
    {
        var factory = new StorageFactory();
        var value = new Arguments { Value = 11 };
        using ShaderRootArguments previous = TestShaderTransfer.Create(in value, factory.Create, Encode);
        var failure = new InvalidOperationException("Selection failed.");
        factory.ThrowOnDispose = true;
        Action<IReadOnlyCollection<IShaderReference>>? Prepare(IShaderReferenceMetadata references)
            => failPrepare ? throw failure : null;
        value.Failure = failPrepare ? null : failure;
        Exception? actual = Record.Exception(() => TestShaderTransfer.Create(in value, factory.Create, Encode, prepare: Prepare));
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
        using ShaderRootArguments previous = TestShaderTransfer.Create(in value, factory.Create, Encode);
        var failure = new InvalidOperationException("Allocation failed.");
        Assert.Same(failure, Record.Exception(() => TestShaderTransfer.Create(in value, _ => throw failure, Encode)));
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
        Assert.Throws<InvalidOperationException>(() => TestShaderTransfer.Create(in value, _ => storage, Encode));
        Assert.Equal(1, storage.Disposes);
        Assert.Equal(0, value.Writes);
    }

    /// <summary>Mandatory bounds and bulk-write invariants do not depend on optional diagnostics.</summary>
    /// <param name="mode">The codec invariant violation to exercise.</param>
    [Theory]
    [InlineData("extra-reference")]
    [InlineData("overflow-prefix")]
    [InlineData("wrong-size")]
    [InlineData("twice")]
    [InlineData("missing")]
    public void InvalidCodecIsRejectedAndStorageDisposed(string mode)
    {
        using var table = new Table();
        var factory = new StorageFactory();
        var value = new Arguments { Mode = mode, Value = 17, Reference = new Reference(table, new object()) };
        int encodes = 0;
        byte[] EncodeSlot(ShaderValue slot)
        {
            encodes++;
            return Encode(slot);
        }

        if (mode is "extra-reference" or "overflow-prefix" or "twice")
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TestShaderTransfer.Create(in value, factory.Create, EncodeSlot));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => TestShaderTransfer.Create(in value, factory.Create, EncodeSlot));
        }

        Storage storage = Assert.Single(factory.Items);
        bool validFirstWire = mode is "extra-reference" or "twice";
        Assert.Equal(validFirstWire ? 1 : 0, encodes);
        Assert.Equal(validFirstWire || mode == "overflow-prefix" ? 2 : mode == "wrong-size" ? 1 : 0, storage.Writes);
        if (validFirstWire)
        {
            Assert.Equal(17u, Read(storage));
            Assert.All(storage.Bytes[16..32], item => Assert.Equal((byte)0x5a, item));
        }

        Assert.Equal(1, storage.Disposes);
        Assert.Equal(0, storage.Seals);
        Assert.Equal(0, value.Probes);
    }

    /// <summary>Reference encoders must produce exactly the common ABI length before any patch is written.</summary>
    /// <param name="length">The invalid encoded reference length in bytes.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void SelectionRejectsWrongReferenceAbiLength(int length)
    {
        using var table = new Table();
        var value = new Arguments { Reference = new Reference(table, new object()) };
        var factory = new StorageFactory();
        Assert.Throws<ArgumentException>(() => TestShaderTransfer.Create(in value, factory.Create, _ => new byte[length]));
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
        using ShaderRootArguments classRoot = TestShaderTransfer.Create(in value, factory.Create, Encode, layout);
        Assert.Equal(1, value.Probes);
        Assert.Equal(19u, Read(factory.Items[0]));
        var numeric = new NumericStruct { Value = 31 };
        using ShaderRootArguments structRoot = TestShaderTransfer.Create(in numeric, factory.Create, Encode, layout);
        Assert.Equal(31u, Read(factory.Items[1]));
    }

    /// <summary>A failing diagnostic provider preserves its original exception and cleans up final storage.</summary>
    [Fact]
    public void DiagnosticFailureDisposesStorageAndPreservesException()
    {
        var failure = new InvalidOperationException("Metadata failed.");
        var value = new NumericArguments { Failure = failure };
        var factory = new StorageFactory { ThrowOnDispose = true };
        Assert.Same(failure, Record.Exception(() => TestShaderTransfer.Create(in value, factory.Create, Encode, DiagnosticLayout())));
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
        var prepared = new List<IShaderReferenceMetadata>();
        var admitted = new List<IReadOnlyCollection<IShaderReference>>();
        Action<IReadOnlyCollection<IShaderReference>>? Prepare(IShaderReferenceMetadata references)
        {
            prepared.Add(references);
            return admitted.Add;
        }

        using ShaderRootArguments root = TestShaderTransfer.Create(in value, factory.Create, Encode, prepare: Prepare);
        IShaderReferenceMetadata current = new ShaderDependencyMetadata([firstReference]);
        int reads = 0;
        IShaderReferenceMetadata ReadMetadata(IShaderDataSource buffer, ulong index)
        {
            Assert.Same(source, buffer);
            Assert.Equal(0ul, index);
            reads++;
            return current;
        }

        var first = ShaderBindingSnapshot.Capture(root, table, ReadMetadata);
        current = new ShaderDependencyMetadata([secondReference]);
        var second = ShaderBindingSnapshot.Capture(root, table, ReadMetadata);
        Assert.Contains(firstReference, first.References);
        Assert.DoesNotContain(secondReference, first.References);
        Assert.Contains(secondReference, second.References);
        Assert.DoesNotContain(firstReference, second.References);
        Assert.Same(rootReference, Assert.Single(root.References.References));
        Assert.NotSame(first.Elements[(source, 0)], second.Elements[(source, 0)]);
        Assert.Equal(2, reads);
        Assert.Equal(2, admitted.Count);
        Assert.Equal(1, value.Writes);
        Assert.Equal(2, factory.Items[0].Writes);
        Assert.Equal(0, source.Reads);
        Assert.Contains(firstReference, admitted[0]);
        Assert.Contains(secondReference, admitted[1]);
    }

    /// <summary>Execution rejection preserves the selected storage and propagates the admission error.</summary>
    [Fact]
    public void AdmissionFailurePreservesRootAndOriginalException()
    {
        var factory = new StorageFactory();
        var value = new Arguments { Value = 37 };
        var failure = new InvalidOperationException("Admission failed.");
        int prepares = 0;
        int admissions = 0;
        Action<IReadOnlyCollection<IShaderReference>>? Prepare(IShaderReferenceMetadata references)
        {
            prepares++;
            return _ =>
            {
                admissions++;
                throw failure;
            };
        }

        using ShaderRootArguments root = TestShaderTransfer.Create(in value, factory.Create, Encode, prepare: Prepare);
        Assert.Same(failure, Record.Exception(() => ShaderBindingSnapshot.Capture(root, null)));
        Assert.Same(failure, Record.Exception(() => ShaderBindingSnapshot.Capture(root, null)));
        Assert.Equal(1, prepares);
        Assert.Equal(2, admissions);
        Assert.Equal(1, value.Writes);
        Assert.Equal(0, factory.Items[0].Disposes);
        Assert.Equal(37u, Read(factory.Items[0]));
    }

    /// <summary>Root disposal is idempotent and prevents subsequent execution.</summary>
    [Fact]
    public void DisposeReleasesFinalStorageOnce()
    {
        var value = new Arguments();
        var factory = new StorageFactory();
        ShaderRootArguments root = TestShaderTransfer.Create(in value, factory.Create, Encode);
        root.Dispose();
        root.Dispose();
        Assert.Equal(1, factory.Items[0].Disposes);
        Assert.Throws<ObjectDisposedException>(() => root.Seal());
        Assert.Throws<ObjectDisposedException>(() => root.Admit([]));
    }

    private static byte[] Encode(ShaderValue value) => Enumerable.Repeat((byte)0x5a, 16).ToArray();

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
            where TWriter : IShaderValueWriter => writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in this, 1)));
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
                writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in small, 1)));
                return;
            }

            var wire = new Wire { Value = Value };
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
            writer.WriteValue(bytes[..16]);
            if (Mode == "overflow-prefix")
            {
                writer.WriteValue(bytes[..1]);
            }

            if (Reference != null)
            {
                writer.WriteReference(Reference);
            }
            else
            {
                writer.WriteValue(bytes[16..]);
            }

            if (Mode is "extra-reference" or "twice")
            {
                writer.WriteReference(Reference);
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
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
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

        public IShaderReferenceMetadata ReadReferences(ulong index)
        {
            Reads++;
            throw new InvalidOperationException("Expected command-local metadata reader.");
        }

        public void SetTransferredReferences(ulong index, IShaderReferenceMetadata value) => throw new NotSupportedException();

        public void ValidateAlive()
        {
        }
    }

    private sealed class ElementLayout : IShaderDataLayout
    {
        public string Name => "Wire";

        public int Size => 16;

        public bool Matches(IShaderDataLayout layout) => ReferenceEquals(this, layout);
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
            where T : IShaderArguments => throw new NotSupportedException();

        public void ReleaseTexture(uint slot) => throw new NotSupportedException();

        public void ReleaseSampler(uint slot) => throw new NotSupportedException();

        public void ReleaseBuffer(uint slot) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
