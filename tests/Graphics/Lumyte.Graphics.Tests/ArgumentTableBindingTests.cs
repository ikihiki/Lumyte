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
        var root = new ShaderValueSnapshot("arguments", "Values", [new("Value", typeof(float), BitConverter.GetBytes(1f), null, false)]);
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
        data.Values[0] = Snapshot(new Reference(foreign, new object()));
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
        data.Values[0] = Snapshot(new Reference(selected, new object()));
        data.Values[1] = Snapshot(new Reference(foreign, new object()));
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
        first.Values[0] = Snapshot(b);
        second.Values[0] = Snapshot(a);
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
        data.Values[0] = Snapshot(new Reference(selected, new object()));
        ShaderValueSnapshot copied = Snapshot(new Reference(foreign, new object()));
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
        var root = new ShaderValueSnapshot("arguments", "Values", [new("Resource", typeof(object), ReadOnlyMemory<byte>.Empty, new object(), true)]);
        Assert.Throws<ArgumentException>(() => ShaderBindingSnapshot.Capture(root, table));
    }

    private static ShaderValueSnapshot Snapshot(IShaderReference reference)
        => new("arguments", "Values", [new("Resource", typeof(object), ReadOnlyMemory<byte>.Empty, reference, true)]);

    private sealed class Reference(IArgumentTable table, object resource, ulong count = 1) : IShaderReference
    {
        public object Table => table;

        public object Resource => resource;

        public uint Slot => 0;

        public ulong OffsetInBytes => 0;

        public ulong SizeInBytes => count * 16;

        public ulong RegistrationOffsetInBytes => 0;

        public ulong RegistrationSizeInBytes => SizeInBytes;

        public ulong Count => count;
    }

    private sealed class DataSource : IShaderDataSource
    {
        public IShaderDataLayout Layout { get; } = new ElementLayout();

        public ulong SizeInBytes => 32;

        public MemoryPreference Memory => MemoryPreference.Automatic;

        public object ShaderHandle => this;

        internal Dictionary<ulong, ShaderValueSnapshot> Values { get; } = [];

        internal int Reads { get; private set; }

        public ShaderValueSnapshot Read(ulong index)
        {
            Reads++;
            return Values[index];
        }

        public void SetTransferredValue(ulong index, ShaderValueSnapshot value) => Values[index] = value;

        public void ValidateAlive()
        {
        }
    }

    private sealed class ElementLayout : IShaderDataLayout
    {
        public string Name => "Values";

        public int Size => 16;

        public string ReferenceKind(string path) => "GpuBufferRef";

        public bool Matches(IShaderDataLayout layout) => ReferenceEquals(this, layout);

        public byte[] Pack(ShaderValueSnapshot snapshot, Func<ShaderValue, string, byte[]> reference) => throw new NotSupportedException();
    }

    private sealed class Table : IArgumentTable
    {
        public uint TextureCapacity => 1;

        public uint SamplerCapacity => 1;

        public uint BufferCapacity => 1;

        public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view) => throw new NotSupportedException();

        public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => throw new NotSupportedException();

        public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range)
            where T : unmanaged => throw new NotSupportedException();

        public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
            where T : struct, IShaderData => throw new NotSupportedException();

        public void ReleaseTexture(uint slot) => throw new NotSupportedException();

        public void ReleaseSampler(uint slot) => throw new NotSupportedException();

        public void ReleaseBuffer(uint slot) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
