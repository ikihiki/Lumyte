using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks one handwritten codec at both synchronous transfer boundaries.</summary>
public sealed class UnifiedManualTransferTests
{
    /// <summary>The same manual class writes independently owned root and upload bytes.</summary>
    [Fact]
    public void ManualClassSupportsRootAndTypedUpload()
    {
        var value = new ManualValue { Value = 7 };
        int encodes = 0;
        byte[] Encode(ShaderValue reference)
        {
            encodes++;
            throw new InvalidOperationException("A numeric-only codec cannot encode references.");
        }

        using TestRootOwner root = TestShaderTransfer.Create(in value, size => new TestRootStorage(size), Encode, diagnostic: null);
        TestRootStorage nativeBacking = Assert.IsType<TestRootStorage>(root.Storage);
        Assert.Equal(4, root.WireSize);
        Assert.Equal(4, nativeBacking.Size);
        Assert.Equal(7u, MemoryMarshal.Read<uint>(nativeBacking.Bytes));
        Assert.All(nativeBacking.Bytes[4..], item => Assert.Equal((byte)0, item));
        Assert.Empty(root.References.References);

        value.Value = 13;
        using var upload = new MappedBuffer(ManualValue.TransferSize);
        var metadata = new IShaderReferenceMetadata?[1];
        TestShaderDataUpload.CopyFrom<ManualValue>(new[] { value }, upload, 0, metadata, Encode, diagnostic: null);
        Assert.Equal(13u, MemoryMarshal.Read<uint>(upload.Bytes));
        Assert.Empty(Assert.IsType<ShaderDependencyMetadata>(metadata[0]).References);

        value.Value = 19;
        Assert.Equal(7u, MemoryMarshal.Read<uint>(nativeBacking.Bytes));
        Assert.Equal(13u, MemoryMarshal.Read<uint>(upload.Bytes));
        Assert.All(nativeBacking.Bytes[4..], item => Assert.Equal((byte)0, item));
        Assert.Empty(root.References.References);
        Assert.Empty(Assert.IsType<ShaderDependencyMetadata>(metadata[0]).References);
        Assert.Equal(0, encodes);

        // Closing the public generic interface proves a class needs only IShaderArguments.
        Assert.Equal(typeof(ManualValue), Assert.Single(typeof(IGraphicsShaderDataBuffer<ManualValue>).GenericTypeArguments));
    }

    private sealed class ManualValue : IShaderArguments
    {
        private uint _value;

        public static string ShaderTypeName => "ManualValue";

        public static int TransferSize => 4;

        public uint Value
        {
            get => _value;
            set => _value = value;
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in _value, 1)));
    }

    private sealed class MappedBuffer(int size) : IGraphicsBuffer<byte>
    {
        public byte[] Bytes { get; } = new byte[size];

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
            ValidateByteRange(offset, length);
            if (length > (ulong)source.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            source[..checked((int)length)].CopyTo(Bytes.AsSpan(checked((int)offset), checked((int)length)));
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
