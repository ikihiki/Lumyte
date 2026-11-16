using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Exercises the actual backend writer with real managed metadata and no native resources.</summary>
public sealed class DirectReferenceWireTests
{
    /// <summary>References use absolute offsets, overwrite all sixteen bytes and preserve adjacent values.</summary>
    /// <param name="prefixSize">The numeric prefix size.</param>
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void ReferenceWritesExactWireBetweenNumericValues(int prefixSize)
    {
        using var device = new Direct3D12Device(null!);
        using Direct3D12ArgumentTable table = CreateTable(device);
        byte[] bytes = Enumerable.Repeat((byte)0xcd, 64).ToArray();
        byte[] prefix = Enumerable.Range(1, prefixSize).Select(value => (byte)value).ToArray();
        byte[] suffix = [0x78, 0x56, 0x34, 0x12];
        var writer = new Direct3D12ShaderValueWriter(device, prefixSize + 20, bytes);
        writer.WriteValue(prefix);
        writer.WriteReference(CreateReference<uint>(table, 7, 12, 3));
        Assert.Equal(prefix, bytes[..prefixSize]);
        Assert.Equal(new byte[] { 7, 0, 0, 0, 12, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0 }, bytes[prefixSize..(prefixSize + 16)]);
        Assert.All(bytes[(prefixSize + 16)..], value => Assert.Equal((byte)0xcd, value));
        writer.WriteValue(suffix);
        writer.Complete();
        Assert.Same(table, writer.Table);
        Assert.Equal(suffix, bytes[(prefixSize + 16)..(prefixSize + 20)]);
        Assert.All(bytes[(prefixSize + 20)..], value => Assert.Equal((byte)0xcd, value));
    }

    /// <summary>Slot, absolute offset and count preserve their complete unsigned 32-bit range.</summary>
    [Fact]
    public void MaximumUInt32FieldsAreLegal()
    {
        using var device = new Direct3D12Device(null!);
        using Direct3D12ArgumentTable table = CreateTable(device);
        byte[] bytes = Enumerable.Repeat((byte)0xcd, 48).ToArray();
        var writer = new Direct3D12ShaderValueWriter(device, 16, bytes);
        writer.WriteReference(CreateReference<uint>(table, uint.MaxValue, uint.MaxValue, uint.MaxValue));
        writer.Complete();
        Assert.Equal(Enumerable.Repeat((byte)0xff, 12).Concat(new byte[4]).ToArray(), bytes[..16]);
        Assert.All(bytes[16..], value => Assert.Equal((byte)0xcd, value));
    }

    /// <summary>Checked field conversion fails before any bytes, position or table selection change.</summary>
    /// <param name="offset">The absolute reference offset.</param>
    /// <param name="count">The logical reference count.</param>
    [Theory]
    [InlineData(ulong.MaxValue, 3UL)]
    [InlineData(12UL, ulong.MaxValue)]
    [InlineData(4294967296UL, 3UL)]
    [InlineData(12UL, 4294967296UL)]
    public void OverflowIsAtomicAndWriterCanRecover(ulong offset, ulong count)
    {
        using var device = new Direct3D12Device(null!);
        using Direct3D12ArgumentTable rejectedTable = CreateTable(device);
        using Direct3D12ArgumentTable acceptedTable = CreateTable(device);
        byte[] bytes = Enumerable.Repeat((byte)0xcd, 48).ToArray();
        var writer = new Direct3D12ShaderValueWriter(device, 24, bytes);
        writer.WriteValue(new byte[] { 1, 2, 3, 4 });
        byte[] before = bytes.ToArray();
        try
        {
            writer.WriteReference(CreateReference<uint>(rejectedTable, 7, offset, count));
            Assert.Fail("Expected checked conversion overflow.");
        }
        catch (OverflowException exception)
        {
            Assert.IsType<OverflowException>(exception);
        }

        Assert.Equal(before, bytes);
        Assert.Null(writer.Table);
        writer.WriteReference(CreateReference<uint>(acceptedTable, 7, 12, 3));
        writer.WriteValue(new byte[] { 5, 6, 7, 8 });
        writer.Complete();
        Assert.Same(acceptedTable, writer.Table);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 7, 0, 0, 0, 12, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 5, 6, 7, 8 }, bytes[..24]);
        Assert.All(bytes[24..], value => Assert.Equal((byte)0xcd, value));
    }

    /// <summary>Invalid reference origins and lifetimes leave the first table selection available.</summary>
    /// <param name="kind">Null, non-backend, foreign-device or disposed-table reference.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectedReferenceDoesNotSelectTableOrMutateBytes(int kind)
    {
        using var device = new Direct3D12Device(null!);
        using var foreignDevice = new Direct3D12Device(null!);
        using Direct3D12ArgumentTable rejectedTable = CreateTable(kind == 2 ? foreignDevice : device);
        using Direct3D12ArgumentTable acceptedTable = CreateTable(device);
        IGpuRef<uint>? reference = kind switch
        {
            0 => null,
            1 => new NonBackendReference(),
            _ => CreateReference<uint>(rejectedTable, 7, 12, 3),
        };
        if (kind == 3)
        {
            rejectedTable.Dispose();
        }

        byte[] bytes = Enumerable.Repeat((byte)0xcd, 48).ToArray();
        var writer = new Direct3D12ShaderValueWriter(device, 20, bytes);
        writer.WriteValue(new byte[] { 1, 2, 3, 4 });
        byte[] before = bytes.ToArray();
        try
        {
            writer.WriteReference(reference);
            Assert.Fail("Expected invalid reference rejection.");
        }
        catch (Exception exception) when (exception is ArgumentException or ObjectDisposedException)
        {
            Assert.Equal(kind == 3 ? typeof(ObjectDisposedException) : typeof(ArgumentException), exception.GetType());
        }

        Assert.Equal(before, bytes);
        Assert.Null(writer.Table);
        writer.WriteReference(CreateReference<uint>(acceptedTable, 7, 12, 3));
        writer.Complete();
        Assert.Same(acceptedTable, writer.Table);
        Assert.Equal(new byte[] { 7, 0, 0, 0, 12, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0 }, bytes[4..20]);
        Assert.All(bytes[20..], value => Assert.Equal((byte)0xcd, value));
    }

    /// <summary>A fifteen-byte tail rejects a reference before examining its origin.</summary>
    /// <param name="useNull">Whether to also prove capacity validation precedes reference semantics.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShortTailIsAtomicAndCanBeFilledNumerically(bool useNull)
    {
        using var device = new Direct3D12Device(null!);
        using Direct3D12ArgumentTable table = CreateTable(device);
        byte[] bytes = Enumerable.Repeat((byte)0xcd, 48).ToArray();
        var writer = new Direct3D12ShaderValueWriter(device, 19, bytes);
        writer.WriteValue(new byte[] { 1, 2, 3, 4 });
        byte[] before = bytes.ToArray();
        try
        {
            writer.WriteReference<uint>(useNull ? null : CreateReference<uint>(table, 7, 12, 3));
            Assert.Fail("Expected capacity rejection.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        Assert.Equal(before, bytes);
        Assert.Null(writer.Table);
        writer.WriteValue(Enumerable.Repeat((byte)0x5a, 15).ToArray());
        writer.Complete();
        Assert.All(bytes[4..19], value => Assert.Equal((byte)0x5a, value));
        Assert.All(bytes[19..], value => Assert.Equal((byte)0xcd, value));
    }

    /// <summary>A selected table remains selected after a conflicting table is rejected.</summary>
    [Fact]
    public void MixedTablesFailWithoutAdvancingPosition()
    {
        using var device = new Direct3D12Device(null!);
        using Direct3D12ArgumentTable table = CreateTable(device);
        using Direct3D12ArgumentTable other = CreateTable(device);
        byte[] bytes = Enumerable.Repeat((byte)0xcd, 48).ToArray();
        var writer = new Direct3D12ShaderValueWriter(device, 32, bytes);
        writer.WriteReference(CreateReference<uint>(table, 7, 12, 3));
        byte[] before = bytes.ToArray();
        try
        {
            writer.WriteReference(CreateReference<uint>(other, 8, 16, 2));
            Assert.Fail("Expected conflicting table rejection.");
        }
        catch (ArgumentException exception)
        {
            Assert.IsType<ArgumentException>(exception);
        }

        Assert.Equal(before, bytes);
        Assert.Same(table, writer.Table);
        writer.WriteReference(CreateReference<uint>(table, 8, 16, 2));
        writer.Complete();
        Assert.Equal(new byte[] { 8, 0, 0, 0, 16, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0 }, bytes[16..32]);
        Assert.All(bytes[32..], value => Assert.Equal((byte)0xcd, value));
    }

    /// <summary>Completed transfers reject both another completion and any further write.</summary>
    [Fact]
    public void CompletionGuardsDoNotModifyDestination()
    {
        using var device = new Direct3D12Device(null!);
        using Direct3D12ArgumentTable table = CreateTable(device);
        byte[] bytes = Enumerable.Repeat((byte)0xcd, 48).ToArray();
        var writer = new Direct3D12ShaderValueWriter(device, 16, bytes);
        writer.WriteReference(CreateReference<uint>(table, 7, 12, 3));
        writer.Complete();
        byte[] before = bytes.ToArray();
        try
        {
            writer.Complete();
            Assert.Fail("Expected repeated completion rejection.");
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            writer.WriteReference(CreateReference<uint>(table, 7, 12, 3));
            Assert.Fail("Expected completed reference write rejection.");
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            writer.WriteValue(new byte[] { 1 });
            Assert.Fail("Expected completed numeric write rejection.");
        }
        catch (InvalidOperationException)
        {
        }

        Assert.Equal(before, bytes);
    }

    /// <summary>The constructor rejects negative logical sizes before any destination access.</summary>
    [Fact]
    public void NegativeSizeIsRejected()
    {
        using var device = new Direct3D12Device(null!);
        byte[] bytes = Enumerable.Repeat((byte)0xcd, 48).ToArray();
        try
        {
            _ = new Direct3D12ShaderValueWriter(device, -1, bytes);
            Assert.Fail("Expected negative-size rejection.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        Assert.All(bytes, value => Assert.Equal((byte)0xcd, value));
    }

    private static Direct3D12ArgumentTable CreateTable(Direct3D12Device device) => new(device, new ArgumentTableDesc { BufferCapacity = 16 });

    private static GpuReference<T> CreateReference<T>(Direct3D12ArgumentTable table, uint slot, ulong offset, ulong count)
    {
        // The real backend constructors are managed metadata only; no native buffer or sampler is fabricated.
        var registration = new ArgumentRegistration(table, slot, new object(), 4, 64, count, ShaderBufferAccess.ReadOnly);
        return new GpuReference<T>(registration, count, 4, offset, 12);
    }

    private sealed class NonBackendReference : IGpuRef<uint>
    {
        public ulong Count => 3;

        public IGpuRef<uint> GetElement(ulong index) => this;
    }
}
