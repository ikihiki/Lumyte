using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises the direct streaming upload helper without a native graphics device.</summary>
public sealed class ShaderDataUploadTests
{
    /// <summary>Failures invalidate the complete requested metadata range and allow a fresh whole-range retry.</summary>
    /// <param name="failureKind">The deterministic failure injection point.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FailedUploadClearsRequestedMetadataAndCanRecover(int failureKind)
    {
        using var storage = new MappedBuffer(4 * UploadValue.TransferSize);
        var first = new UploadValue { Number = 17, Reference = new TestReference(1) };
        var second = new UploadValue { Number = 29, Reference = new TestReference(2) };
        UploadValue[] source = [first, second];
        IShaderReferenceMetadata?[] metadata = SeedMetadata();
        IShaderReferenceMetadata? before = metadata[0];
        IShaderReferenceMetadata? after = metadata[3];
        var failure = new InvalidOperationException("Injected upload failure.");
        int encoded = 0;
        if (failureKind == 0)
        {
            second.Failure = failure;
        }
        else if (failureKind == 2)
        {
            storage.Failure = failure;
        }

        byte[] Encode(ShaderValue value)
        {
            encoded++;
            if (failureKind == 1 && encoded == 2)
            {
                throw failure;
            }

            return EncodeReference(value);
        }

        Exception? actual = Record.Exception(() => ShaderDataUpload.CopyFrom<UploadValue>(source, storage, 1, metadata, Encode, null));
        Assert.Same(failure, actual);
        if (failureKind != 2)
        {
            Assert.Equal(1, first.Writes);
            Assert.Equal(1, second.Writes);
        }
        else
        {
            Assert.Equal(1, storage.PartialCopies);
        }

        Assert.Null(metadata[1]);
        Assert.Null(metadata[2]);
        Assert.Same(before, metadata[0]);
        Assert.Same(after, metadata[3]);
        if (failureKind == 1)
        {
            Assert.Equal(2, encoded);
        }

        // Bytes inside the failed range are intentionally unspecified, not atomic.
        AssertOuterBytes(storage);
        Assert.Equal(0, storage.OutOfBoundsWrites);
        Assert.Equal(0, first.Probes);
        Assert.Equal(0, second.Probes);
        first.Number = 101;
        second.Number = 203;
        first.Reference = new TestReference(11);
        second.Reference = new TestReference(12);
        second.Failure = null;
        storage.Failure = null;
        storage.CopyWriteCalls.Clear();
        first.Writes = 0;
        second.Writes = 0;

        ShaderDataUpload.CopyFrom<UploadValue>(source, storage, 1, metadata, EncodeReference, null);

        Assert.Equal(1, first.Writes);
        Assert.Equal(1, second.Writes);
        Assert.Equal(0, first.Probes);
        Assert.Equal(0, second.Probes);
        Assert.Equal(4, storage.CopyWriteCalls.Count);
        AssertElement(storage, 1, first);
        AssertElement(storage, 2, second);
        AssertReferenceMetadata(metadata[1], first.Reference);
        AssertReferenceMetadata(metadata[2], second.Reference);
        Assert.Same(before, metadata[0]);
        Assert.Same(after, metadata[3]);
        AssertOuterBytes(storage);
    }

    /// <summary>Host-invalid batch ranges are rejected before codec, encoder or destination writes.</summary>
    /// <param name="rangeKind">The invalid metadata, allocation or overflowing host-size range.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidHostRangesNeverWrite(int rangeKind)
    {
        using var storage = new MappedBuffer(UploadValue.TransferSize);
        IShaderReferenceMetadata?[] metadata = SeedMetadata();
        IShaderReferenceMetadata?[] original = metadata.ToArray();
        var value = new UploadValue();
        int encoded = 0;
        byte[] Encode(ShaderValue reference)
        {
            encoded++;
            return EncodeReference(reference);
        }

        Exception? failure;
        if (rangeKind == 3)
        {
            HugeValue[] source = [new(), new()];
            failure = Record.Exception(() => ShaderDataUpload.CopyFrom<HugeValue>(source, storage, 0, metadata, Encode, null));
            Assert.IsType<OverflowException>(failure);
        }
        else
        {
            ulong offset = rangeKind == 0 ? ulong.MaxValue : rangeKind == 1 ? 4UL : 1UL;
            failure = Record.Exception(() => ShaderDataUpload.CopyFrom<UploadValue>(new[] { value }, storage, offset, metadata, Encode, null));
            Assert.IsType<ArgumentOutOfRangeException>(failure);
        }

        Assert.Equal(0, value.Writes);
        Assert.Equal(0, encoded);
        Assert.Empty(storage.CopyWriteCalls);
        Assert.Equal(0, storage.OutOfBoundsWrites);
        Assert.All(storage.Bytes, item => Assert.Equal((byte)0xCD, item));
        for (int i = 0; i < metadata.Length; i++)
        {
            Assert.Same(original[i], metadata[i]);
        }
    }

    /// <summary>Malformed slots fail before encoding or any out-of-bounds write, without demanding byte rollback.</summary>
    /// <param name="slot">The invalid reference offset, including a duplicate valid slot.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(int.MaxValue)]
    public void InvalidSlotsClearMetadataWithoutUnsafeWrites(int slot)
    {
        using var storage = new MappedBuffer(4 * UploadValue.TransferSize);
        IShaderReferenceMetadata?[] metadata = SeedMetadata();
        IShaderReferenceMetadata? before = metadata[0];
        IShaderReferenceMetadata? after = metadata[3];
        var value = new UploadValue { ExtraSlot = slot };
        int encoded = 0;
        byte[] Encode(ShaderValue reference)
        {
            encoded++;
            return EncodeReference(reference);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => ShaderDataUpload.CopyFrom<UploadValue>(new[] { value, new UploadValue() }, storage, 1, metadata, Encode, null));
        Assert.Equal(1, encoded);
        Assert.Null(metadata[1]);
        Assert.Null(metadata[2]);
        Assert.Same(before, metadata[0]);
        Assert.Same(after, metadata[3]);
        Assert.Equal(0, storage.OutOfBoundsWrites);
        AssertOuterBytes(storage);
    }

    private static IShaderReferenceMetadata?[] SeedMetadata()
        => Enumerable.Range(0, 4).Select(_ => (IShaderReferenceMetadata?)new ShaderDependencyMetadata([])).ToArray();

    private static byte[] EncodeReference(ShaderValue value)
        => Enumerable.Repeat((byte)Assert.IsType<TestReference>(value.Reference).Count, 16).ToArray();

    private static void AssertElement(MappedBuffer storage, int index, UploadValue value)
    {
        int offset = index * UploadValue.TransferSize;
        Assert.Equal(value.Number, MemoryMarshal.Read<uint>(storage.Bytes.AsSpan(offset, sizeof(uint))));
        Assert.All(storage.Bytes.AsSpan(offset + 4, 16).ToArray(), item => Assert.Equal((byte)value.Reference.Count, item));
    }

    private static void AssertReferenceMetadata(IShaderReferenceMetadata? dependencies, IGpuRef<uint> reference)
    {
        ShaderDependencyMetadata metadata = Assert.IsType<ShaderDependencyMetadata>(dependencies);
        IShaderReference dependency = Assert.Single(metadata.References);
        TestReference expected = Assert.IsType<TestReference>(reference);
        Assert.Same(expected, dependency);
        Assert.IsAssignableFrom<IGpuRef<uint>>(dependency);
        Assert.Same(expected.Table, dependency.Table);
        Assert.Same(expected.Resource, dependency.Resource);
        Assert.Equal(ShaderBufferAccess.ReadWrite, dependency.BufferAccess);
        Assert.Equal(3U, dependency.Slot);
        Assert.Equal(8UL, dependency.OffsetInBytes);
        Assert.Equal(reference.Count * sizeof(uint), dependency.SizeInBytes);
        Assert.Equal(8UL, dependency.RegistrationOffsetInBytes);
        Assert.Equal(reference.Count * sizeof(uint), dependency.RegistrationSizeInBytes);
        Assert.Equal(reference.Count, dependency.Count);
        IList<IShaderReference> references = Assert.IsAssignableFrom<IList<IShaderReference>>(metadata.References);
        Assert.True(references.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => references[0] = new TestReference(99));
        PropertyInfo property = Assert.Single(typeof(ShaderDependencyMetadata).GetProperties());
        Assert.Equal(nameof(ShaderDependencyMetadata.References), property.Name);
        Assert.Equal(typeof(IReadOnlyList<IShaderReference>), property.PropertyType);
        FieldInfo field = Assert.Single(typeof(ShaderDependencyMetadata).GetFields(BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.Equal(typeof(IReadOnlyList<IShaderReference>), field.FieldType);
        Assert.Same(metadata.References, field.GetValue(metadata));
    }

    private static void AssertOuterBytes(MappedBuffer storage)
    {
        Assert.All(storage.Bytes[..UploadValue.TransferSize], item => Assert.Equal((byte)0xCD, item));
        Assert.All(storage.Bytes[(3 * UploadValue.TransferSize)..], item => Assert.Equal((byte)0xCD, item));
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct WireValue
    {
        public uint Number;
        public ShaderReferenceSlot Reference;
    }

    private sealed class UploadValue : IShaderData
    {
        public static string ShaderTypeName => throw new InvalidOperationException("Type diagnostics must remain disabled.");

        public static int TransferSize => 20;

        public static IReadOnlyList<ShaderTransferField> TransferFields => throw new InvalidOperationException("Static diagnostics must remain disabled.");

        public uint Number { get; set; }

        public IGpuRef<uint> Reference { get; set; } = new TestReference(1);

        public Exception? Failure { get; set; }

        public int? ExtraSlot { get; set; }

        public int Writes { get; set; }

        public int Probes { get; private set; }

        public IReadOnlyList<ShaderTransferField> GetTransferFields()
        {
            Probes++;
            throw new InvalidOperationException("Instance diagnostics must remain disabled.");
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            Writes++;
            if (Failure != null)
            {
                throw Failure;
            }

            var wire = new WireValue { Number = Number, Reference = default };
            var emitter = new References(this);
            ShaderWire.Write(in wire, ref emitter, ref writer);
        }

        private readonly struct References(UploadValue owner) : IShaderReferenceEmitter
        {
            public void Emit<TCursor>(ref TCursor cursor)
                where TCursor : IShaderReferenceCursor, allows ref struct
            {
                cursor.WriteReference(4, owner.Reference);
                if (owner.ExtraSlot is int slot)
                {
                    cursor.WriteReference(slot, owner.Reference);
                }
            }
        }
    }

    private sealed class HugeValue : IShaderData
    {
        public static string ShaderTypeName => throw new InvalidOperationException("No diagnostics expected.");

        public static int TransferSize => int.MaxValue;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => throw new InvalidOperationException("Overflow must precede codec execution.");
    }

    private sealed class TestReference(ulong count) : IGpuRef<uint>, IShaderReference
    {
        public object Table { get; } = new();

        public object Resource { get; } = new();

        public ShaderBufferAccess BufferAccess => ShaderBufferAccess.ReadWrite;

        public uint Slot => 3;

        public ulong OffsetInBytes => 8;

        public ulong SizeInBytes => checked(Count * sizeof(uint));

        public ulong RegistrationOffsetInBytes => OffsetInBytes;

        public ulong RegistrationSizeInBytes => SizeInBytes;

        public ulong Count => count;

        public IGpuRef<uint> GetElement(ulong index) => throw new NotSupportedException();
    }

    private sealed class MappedBuffer(int size) : IGraphicsBuffer<byte>
    {
        public byte[] Bytes { get; } = Enumerable.Repeat((byte)0xCD, size).ToArray();

        public List<(ulong Offset, ulong Length)> CopyWriteCalls { get; } = [];

        public Exception? Failure { get; set; }

        public int OutOfBoundsWrites { get; private set; }

        public int PartialCopies { get; private set; }

        public ulong SizeInBytes => (ulong)Bytes.Length;

        public BufferLayout<byte> Layout => new(1, 1, 1, 1);

        public ulong Count => SizeInBytes;

        public BufferUsage Usage => BufferUsage.CopySource;

        public MemoryPreference Memory => MemoryPreference.Upload;

        public bool IsMapped => true;

        public void ValidateByteRange(ulong offset, ulong length)
        {
            if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
        }

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
        {
            CopyWriteCalls.Add((offset, length));
            if (offset > SizeInBytes || length > SizeInBytes - offset || length > (ulong)source.Length)
            {
                OutOfBoundsWrites++;
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            int copied = Failure == null ? checked((int)length) : Math.Min(3, checked((int)length));
            source[..copied].CopyTo(Bytes.AsSpan(checked((int)offset), copied));
            if (Failure != null)
            {
                PartialCopies++;
                throw Failure;
            }
        }

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException();

        public ValueTask MapAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Unmap() => throw new NotSupportedException();

        public BufferSlice<byte> SliceElements(ulong offset, ulong count) => throw new NotSupportedException();

        public void CopyFrom(ReadOnlySpan<byte> source) => throw new NotSupportedException();

        public void CopyTo(Span<byte> destination) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
