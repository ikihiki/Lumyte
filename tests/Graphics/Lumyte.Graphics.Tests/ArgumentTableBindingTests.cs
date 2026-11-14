using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks the selected argument table across all reachable shader references without a GPU.</summary>
public sealed class ArgumentTableBindingTests
{
    /// <summary>Supplies the immutable reference-slot count for a specialized test wire type.</summary>
    private interface IReferenceCount
    {
        /// <summary>Gets the exact number of wire slots.</summary>
        static abstract int Count { get; }
    }

    /// <summary>Accepts a reference issued by the selected table.</summary>
    [Fact]
    public void SelectedTableReferenceIsAccepted()
    {
        using var table = new Table();
        var reference = new Reference(table, new object());
        var captured = ShaderBindingDependencies.Collect(Root(reference), table);
        Assert.Same(reference, Assert.Single(captured.References));
    }

    /// <summary>The same slot and resource in a different table do not authorize its reference.</summary>
    [Fact]
    public void ForeignTableReferenceIsRejected()
    {
        using var selected = new Table();
        using var foreign = new Table();
        object resource = new();
        var allowed = new Reference(selected, resource);
        var outside = new Reference(foreign, resource);
        Assert.Single(ShaderBindingDependencies.Collect(Root(allowed), selected).References);
        Assert.Throws<ArgumentException>(() => ShaderBindingDependencies.Collect(Root(outside), selected));
    }

    /// <summary>Resource references require an explicitly selected table.</summary>
    [Fact]
    public void MissingTableIsRejected()
    {
        using var table = new Table();
        Assert.Throws<InvalidOperationException>(() => ShaderBindingDependencies.Collect(Root(new Reference(table, new object())), null));
    }

    /// <summary>Numeric arguments remain independent of argument tables.</summary>
    [Fact]
    public void ValueOnlyArgumentsDoNotRequireTable()
    {
        var root = ShaderRootArguments.Create("arguments", "Values", 4, new ShaderDependencyMetadata([]));
        var captured = ShaderBindingDependencies.Collect(root, null);
        Assert.Empty(captured.References);
        Assert.Empty(captured.Buffers);
    }

    /// <summary>Checks resources reached through shader data as well as direct root references.</summary>
    [Fact]
    public void TransitiveForeignReferenceIsRejected()
    {
        using var selected = new Table();
        using var foreign = new Table();
        var data = new DataSource();
        data.Values[0] = new ShaderDependencyMetadata([new Reference(foreign, new object())]);
        Assert.Throws<ArgumentException>(() => ShaderBindingDependencies.Collect(Root(new Reference(selected, data)), selected));
        Assert.Equal(1, data.Reads);
    }

    /// <summary>Rejects a foreign source before reading its element metadata.</summary>
    [Fact]
    public void ForeignSourceIsRejectedBeforeRead()
    {
        using var selected = new Table();
        using var foreign = new Table();
        var data = new DataSource();
        Assert.Throws<ArgumentException>(() => ShaderBindingDependencies.Collect(Root(new Reference(foreign, data)), selected));
        Assert.Equal(0, data.Reads);
    }

    /// <summary>Whole-buffer dependencies include references outside the selected element range.</summary>
    [Fact]
    public void ForeignReferenceOutsideSelectedRangeIsRejected()
    {
        using var selected = new Table();
        using var foreign = new Table();
        var data = new DataSource();
        data.Values[0] = new ShaderDependencyMetadata([new Reference(selected, new object())]);
        data.Values[1] = new ShaderDependencyMetadata([new Reference(foreign, new object())]);
        Assert.Throws<ArgumentException>(() => ShaderBindingDependencies.Collect(Root(new Reference(selected, data)), selected));
        Assert.Equal(1, data.Reads);
    }

    /// <summary>Cycles inside the selected table terminate and capture each whole buffer once.</summary>
    [Fact]
    public void SelectedTableCyclesAreAccepted()
    {
        using var table = new Table();
        var first = new DataSource();
        var second = new DataSource();
        var a = new Reference(table, first);
        var b = new Reference(table, second);
        first.Values[0] = new ShaderDependencyMetadata([b]);
        second.Values[0] = new ShaderDependencyMetadata([a]);
        var captured = ShaderBindingDependencies.Collect(Root(a), table);
        Assert.Equal(2, captured.References.Count);
        Assert.Equal(2, captured.Buffers.Count);
        Assert.Equal(1, first.Reads);
        Assert.Equal(1, second.Reads);
    }

    /// <summary>Explicit copies use command-local metadata when validating transitive references.</summary>
    [Fact]
    public void TransferredMetadataIsValidated()
    {
        using var selected = new Table();
        using var foreign = new Table();
        var data = new DataSource();
        data.Values[0] = new ShaderDependencyMetadata([new Reference(selected, new object())]);
        IShaderReferenceMetadata copied = new ShaderDependencyMetadata([new Reference(foreign, new object())]);
        Assert.Throws<ArgumentException>(() => ShaderBindingDependencies.Collect(Root(new Reference(selected, data)), selected, _ => copied));
        Assert.Equal(0, data.Reads);
    }

    /// <summary>Changing the selected table revalidates an existing root snapshot.</summary>
    [Fact]
    public void TableSwitchRejectsPreviousReferences()
    {
        using var first = new Table();
        using var second = new Table();
        ShaderRootArguments root = Root(new Reference(first, new object()));
        Assert.Single(ShaderBindingDependencies.Collect(root, first).References);
        Assert.Throws<ArgumentException>(() => ShaderBindingDependencies.Collect(root, second));
    }

    /// <summary>Dependency metadata rejects missing identity lists and null identities.</summary>
    [Fact]
    public void InvalidReferenceIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ShaderDependencyMetadata(null!));
        Assert.Throws<ArgumentException>(() => new ShaderDependencyMetadata([null!]));
    }

    /// <summary>Aliased storage keeps distinct read/write pools and remap lanes per registration.</summary>
    /// <param name="mismatch">Whether registrations intentionally disagree with shader access declarations.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationAccessChoosesPoolsAndRemapLanes(bool mismatch)
    {
        using var table = new Table();
        var first = new RawSource();
        var second = new RawSource();
        string[] names = ["Read0", "Write0", "Read1", "Write1"];
        var values = new ShaderValue[4];
        var fields = new ShaderTransferField[4];
        for (int i = 0; i < values.Length; i++)
        {
            var reference = new Reference(table, i < 2 ? first : second, slot: (uint)i, access: (i % 2 == 0) != mismatch ? ShaderBufferAccess.ReadOnly : ShaderBufferAccess.ReadWrite);
            values[i] = new(typeof(uint), reference, i * 16);
            fields[i] = new(names[i], i * 16, 16, typeof(ShaderReferenceSlot), true);
        }

        string json = JsonSerializer.Serialize(new
        {
            lumyteReferenceTargets = names.ToDictionary(name => "Pools." + name, _ => "uint"),
            parameters = new[]
            {
                new
                {
                    name = "arguments",
                    type = new
                    {
                        kind = "constantBuffer",
                        elementType = new
                        {
                            kind = "struct",
                            name = "Pools",
                            sizes = new[] { new { kind = "uniform", value = 64, alignment = 4 } },
                            fields = names.Select((name, i) => new
                            {
                                name,
                                type = new { kind = "struct", name = i % 2 == 0 ? "GpuBufferRef" : "GpuRWBufferRef" },
                                binding = new { offset = i * 16, size = 16 },
                            }).ToArray(),
                        },
                    },
                },
            },
        });
        var target = new ShaderTargetData(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", "row-major", [], json);
        var root = new ShaderValueSnapshot("arguments", "Pools", new byte[64], values, fields);
        var caps = new DeviceCaps { MaxStorageBufferBindingSize = 1024, MaxStorageBuffersPerStage = 8, MaxUniformBuffersPerStage = 1 };
        var layout = ShaderDataLayout.Root(target, "arguments");
        if (mismatch)
        {
            Assert.Throws<ArgumentException>(() => layout.ValidateArguments(root, validateReferences: true));
        }
        else
        {
            layout.ValidateArguments(root, validateReferences: true);
        }

        layout.ValidateArguments(root);
        var arguments = new PoolArguments<FourReferences>(values.Select(value => (IGpuRef<uint>)value.Reference!).ToArray());
        using TestRootOwner generation = TestShaderTransfer.Create(in arguments, size => new TestRootStorage(size), Encode);
        var binding = new ShaderBindingData(values.Select(value => (IShaderReference)value.Reference!).ToArray(), target, caps);
        Assert.Equal(2, binding.Buffers.Count);
        Assert.Equal(2, binding.Writable.Count);
        Assert.Equal(mismatch ? 0u : 1u, BinaryPrimitives.ReadUInt32LittleEndian(binding.Map.AsSpan((2 * 16) + 8)));
        Assert.Equal(mismatch ? 1u : 0u, BinaryPrimitives.ReadUInt32LittleEndian(binding.Map.AsSpan((2 * 16) + 12)));
        Assert.Equal(mismatch ? 1u : 0u, BinaryPrimitives.ReadUInt32LittleEndian(binding.Map.AsSpan((3 * 16) + 8)));
        Assert.Equal(mismatch ? 0u : 1u, BinaryPrimitives.ReadUInt32LittleEndian(binding.Map.AsSpan((3 * 16) + 12)));
        for (int i = 0; i < values.Length; i++)
        {
            Assert.Equal((uint)i, BinaryPrimitives.ReadUInt32LittleEndian(Assert.IsType<TestRootStorage>(generation.Storage).Bytes.AsSpan(i * 16)));
        }
    }

    /// <summary>All written registrations bind even when absent from the root, including sparse slots.</summary>
    [Fact]
    public void WholeTablePackingIncludesUnreachableRegistrationsAndSlotGaps()
    {
        using var table = new Table();
        var first = new RawSource();
        var second = new RawSource();
        IShaderReference[] registrations =
        [
            new Reference(table, first, slot: 1),
            new Reference(table, second, slot: 5),
            new Reference(table, second, slot: 9, access: ShaderBufferAccess.ReadWrite),
        ];
        var root = ShaderRootArguments.Create("arguments", "Values", 4, new ShaderDependencyMetadata([]));
        Assert.Empty(ShaderBindingDependencies.Collect(root, table).References);
        var target = new ShaderTargetData(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", "row-major", [], "not JSON");
        var caps = new DeviceCaps { MaxStorageBufferBindingSize = 1024, MaxStorageBuffersPerStage = 8 };
        var binding = new ShaderBindingData(registrations, target, caps);
        Assert.Equal(2, binding.Buffers.Count);
        Assert.Single(binding.Writable);
        Assert.Equal(10 * 16, binding.Map.Length);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(binding.Map.AsSpan((5 * 16) + 8)));
        Assert.All(binding.Map.AsSpan(2 * 16, 3 * 16).ToArray(), item => Assert.Equal((byte)0, item));
        Assert.Contains(new ShaderBufferBinding(second, 0, 16), binding.Writable.Keys);
    }

    /// <summary>Unavailable dependencies are not confused with a known empty numeric buffer.</summary>
    [Fact]
    public void UnavailableBufferDependenciesAreRejected()
    {
        using var table = new Table();
        var source = new DataSource { Tracking = false };
        Assert.False(source.TryGetDependencies(out IShaderReferenceMetadata? dependencies));
        Assert.Null(dependencies);
        Assert.Throws<NotSupportedException>(() => ShaderBindingDependencies.Collect(Root(new Reference(table, source)), table));
        Assert.Throws<NotSupportedException>(() => ShaderBindingDependencies.Collect(Root(new Reference(table, source)), table, _ => null));
    }

    private static ShaderRootArguments Root(IShaderReference reference) =>
        ShaderRootArguments.Create("arguments", "Pools", 64, new ShaderDependencyMetadata([reference]));

    private static byte[] Encode(ShaderValue value)
    {
        byte[] bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, ((IShaderReference)value.Reference!).Slot);
        return bytes;
    }

    private readonly struct FourReferences : IReferenceCount
    {
        public static int Count => 4;
    }

    private struct PoolArguments<TCount>(IGpuRef<uint>[] references) : IShaderArguments
        where TCount : struct, IReferenceCount
    {
        public static string ShaderTypeName => "Pools";

        public static int TransferSize => 64;

        public static int ReferenceCount => TCount.Count;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            Wire wire = default;
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
            if (references.Length != TCount.Count)
            {
                throw new ArgumentException("Reference array must match the specialized wire type.");
            }

            foreach (IGpuRef<uint> reference in references)
            {
                writer.WriteReference(reference);
            }

            if (references.Length < 4)
            {
                writer.WriteValue(bytes[(references.Length * 16)..]);
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Size = 64)]
        private struct Wire
        {
            public uint Padding;
        }
    }

    private sealed class Reference(IArgumentTable table, object resource, ulong count = 1, uint slot = 0, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly) : IShaderReference, IGpuRef<uint>
    {
        public object Table => table;

        public ShaderBufferAccess BufferAccess => access;

        public object Resource => resource;

        public uint Slot => slot;

        public ulong OffsetInBytes => 0;

        public ulong SizeInBytes => count * 16;

        public ulong RegistrationOffsetInBytes => 0;

        public ulong RegistrationSizeInBytes => SizeInBytes;

        public ulong Count => count;

        public IGpuRef<uint> GetElement(ulong index) => throw new NotSupportedException();
    }

    private sealed class RawSource : IShaderRawBuffer
    {
        public Type ElementType => typeof(uint);

        public string ShaderTypeName => "uint";

        public BufferUsage Usage => BufferUsage.ShaderRead | BufferUsage.ShaderWrite;

        public ulong SizeInBytes => 16;

        public object ShaderHandle => this;
    }

    private sealed class DataSource : IShaderDataSource
    {
        public IShaderDataLayout Layout { get; } = new ElementLayout();

        public ulong SizeInBytes => 32;

        public MemoryPreference Memory => MemoryPreference.Automatic;

        public object ShaderHandle => this;

        internal Dictionary<ulong, IShaderReferenceMetadata> Values { get; } = [];

        internal int Reads { get; private set; }

        internal bool Tracking { get; init; } = true;

        public bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies)
        {
            Reads++;
            if (!Tracking)
            {
                dependencies = null;
                return false;
            }

            dependencies = new ShaderDependencyMetadata(Values.Values.SelectMany(value => value.References));
            return true;
        }

        public void ValidateAlive()
        {
        }
    }

    private sealed class ElementLayout : IShaderDataLayout
    {
        public string Name => "Values";

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
            where T : struct, IShaderArguments => throw new NotSupportedException();

        public void ReleaseTexture(uint slot) => throw new NotSupportedException();

        public void ReleaseSampler(uint slot) => throw new NotSupportedException();

        public void ReleaseBuffer(uint slot) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
