using System.Buffers.Binary;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks the selected argument table across all reachable shader references without a GPU.</summary>
public sealed class ArgumentTableBindingTests
{
    /// <summary>Accepts a reference issued by the selected table.</summary>
    [Fact]
    public void SelectedTableReferenceIsAccepted()
    {
        using var table = new Table();
        var reference = new Reference(table, new object());
        var captured = ShaderBindingSnapshot.Capture(Snapshot(reference), table);
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
        Assert.Single(ShaderBindingSnapshot.Capture(Snapshot(allowed), selected).References);
        Assert.Throws<ArgumentException>(() => ShaderBindingSnapshot.Capture(Snapshot(outside), selected));
    }

    /// <summary>Resource references require an explicitly selected table.</summary>
    [Fact]
    public void MissingTableIsRejected()
    {
        using var table = new Table();
        Assert.Throws<InvalidOperationException>(() => ShaderBindingSnapshot.Capture(Snapshot(new Reference(table, new object())), null));
    }

    /// <summary>Numeric arguments remain independent of argument tables.</summary>
    [Fact]
    public void ValueOnlyArgumentsDoNotRequireTable()
    {
        var root = new ShaderValueSnapshot("arguments", "Values", BitConverter.GetBytes(1f), [], [new("Value", 0, sizeof(float), typeof(float))]);
        var captured = ShaderBindingSnapshot.Capture(root, null);
        Assert.Empty(captured.References);
        Assert.Empty(captured.Elements);
    }

    /// <summary>Checks resources reached through shader data as well as direct root references.</summary>
    [Fact]
    public void TransitiveForeignReferenceIsRejected()
    {
        using var selected = new Table();
        using var foreign = new Table();
        var data = new DataSource();
        data.Values[0] = new ShaderReferenceMetadata(Snapshot(new Reference(foreign, new object())).Values);
        Assert.Throws<ArgumentException>(() => ShaderBindingSnapshot.Capture(Snapshot(new Reference(selected, data)), selected));
        Assert.Equal(1, data.Reads);
    }

    /// <summary>Rejects a foreign source before reading its element metadata.</summary>
    [Fact]
    public void ForeignSourceIsRejectedBeforeRead()
    {
        using var selected = new Table();
        using var foreign = new Table();
        var data = new DataSource();
        Assert.Throws<ArgumentException>(() => ShaderBindingSnapshot.Capture(Snapshot(new Reference(foreign, data)), selected));
        Assert.Equal(0, data.Reads);
    }

    /// <summary>References in every element of a registered range are checked.</summary>
    [Fact]
    public void ForeignReferenceInLaterElementIsRejected()
    {
        using var selected = new Table();
        using var foreign = new Table();
        var data = new DataSource();
        data.Values[0] = new ShaderReferenceMetadata(Snapshot(new Reference(selected, new object())).Values);
        data.Values[1] = new ShaderReferenceMetadata(Snapshot(new Reference(foreign, new object())).Values);
        Assert.Throws<ArgumentException>(() => ShaderBindingSnapshot.Capture(Snapshot(new Reference(selected, data, 2)), selected));
        Assert.Equal(2, data.Reads);
    }

    /// <summary>Cycles inside the selected table terminate and capture each element once.</summary>
    [Fact]
    public void SelectedTableCyclesAreAccepted()
    {
        using var table = new Table();
        var first = new DataSource();
        var second = new DataSource();
        var a = new Reference(table, first);
        var b = new Reference(table, second);
        first.Values[0] = new ShaderReferenceMetadata(Snapshot(b).Values);
        second.Values[0] = new ShaderReferenceMetadata(Snapshot(a).Values);
        var captured = ShaderBindingSnapshot.Capture(Snapshot(a), table);
        Assert.Equal(2, captured.References.Count);
        Assert.Equal(2, captured.Elements.Count);
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
        data.Values[0] = new ShaderReferenceMetadata(Snapshot(new Reference(selected, new object())).Values);
        ShaderReferenceMetadata copied = new(Snapshot(new Reference(foreign, new object())).Values);
        Assert.Throws<ArgumentException>(() => ShaderBindingSnapshot.Capture(Snapshot(new Reference(selected, data)), selected, (_, _) => copied));
        Assert.Equal(0, data.Reads);
    }

    /// <summary>Changing the selected table revalidates an existing root snapshot.</summary>
    [Fact]
    public void TableSwitchRejectsPreviousReferences()
    {
        using var first = new Table();
        using var second = new Table();
        ShaderValueSnapshot root = Snapshot(new Reference(first, new object()));
        Assert.Single(ShaderBindingSnapshot.Capture(root, first).References);
        Assert.Throws<ArgumentException>(() => ShaderBindingSnapshot.Capture(root, second));
    }

    /// <summary>Missing backend reference metadata is rejected before binding.</summary>
    [Fact]
    public void InvalidReferenceIsRejected()
    {
        using var table = new Table();
        var root = new ShaderValueSnapshot("arguments", "Values", new byte[16], [new(typeof(object), new object(), 0)], [new("Resource", 0, 16, typeof(ShaderReferenceSlot), true)]);
        Assert.Throws<ArgumentException>(() => ShaderBindingSnapshot.Capture(root, table));
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
        var binding = new ShaderBindingData(ShaderBindingSnapshot.Capture(root, table), target, caps);
        Assert.Equal(2, binding.Buffers.Count);
        Assert.Equal(2, binding.Writable.Count);
        Assert.Equal(mismatch ? 0u : 1u, BinaryPrimitives.ReadUInt32LittleEndian(binding.Map.AsSpan((2 * 16) + 8)));
        Assert.Equal(mismatch ? 1u : 0u, BinaryPrimitives.ReadUInt32LittleEndian(binding.Map.AsSpan((2 * 16) + 12)));
        Assert.Equal(mismatch ? 1u : 0u, BinaryPrimitives.ReadUInt32LittleEndian(binding.Map.AsSpan((3 * 16) + 8)));
        Assert.Equal(mismatch ? 0u : 1u, BinaryPrimitives.ReadUInt32LittleEndian(binding.Map.AsSpan((3 * 16) + 12)));
        for (int i = 0; i < values.Length; i++)
        {
            Assert.Equal((uint)i, BinaryPrimitives.ReadUInt32LittleEndian(binding.Root.AsSpan(i * 16)));
        }
    }

    private static ShaderValueSnapshot Snapshot(IShaderReference reference)
        => new("arguments", "Values", new byte[16], [new(typeof(object), reference, 0)], [new("Resource", 0, 16, typeof(ShaderReferenceSlot), true)]);

    private sealed class Reference(IArgumentTable table, object resource, ulong count = 1, uint slot = 0, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly) : IShaderReference
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

        public IShaderReferenceMetadata ReadReferences(ulong index)
        {
            Reads++;
            return Values[index];
        }

        public void SetTransferredReferences(ulong index, IShaderReferenceMetadata value) => Values[index] = value;

        public void ValidateAlive()
        {
        }
    }

    private sealed class ElementLayout : IShaderDataLayout
    {
        public string Name => "Values";

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
