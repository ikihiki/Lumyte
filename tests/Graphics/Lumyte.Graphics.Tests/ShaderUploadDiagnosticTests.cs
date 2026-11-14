using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Preserves explicit direct-upload diagnostics for handwritten argument providers.</summary>
public sealed class ShaderUploadDiagnosticTests
{
    /// <summary>Reference-type uploads query instance-only metadata exactly when diagnostics are enabled.</summary>
    /// <param name="enabled">Whether a compiled diagnostic layout is supplied.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassInstanceMetadataRemainsCompatible(bool enabled)
    {
        using var storage = new MappedBuffer();
        var value = new InstanceData { Number = 37 };
        var metadata = new IShaderReferenceMetadata?[1];
        ShaderDataLayout layout = CreateLayout();

        // The previous streaming call used this empty static default and rejected the valid provider.
        Assert.Empty(StaticFields<InstanceData>());
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            Assert.Throws<ArgumentException>(() => layout.ValidateTransferLayout(4, StaticFields<InstanceData>(), "UploadNumber"));
        }

        ShaderDataUpload.CopyFrom<InstanceData>(new[] { value }, storage, 0, metadata, UnexpectedReference, enabled ? layout : null);

        Assert.Equal(enabled ? 1 : 0, value.Probes);
        Assert.Equal(37U, MemoryMarshal.Read<uint>(storage.Bytes));
        Assert.Empty(Assert.IsAssignableFrom<IShaderReferenceMetadata>(metadata[0]).References);
    }

    /// <summary>Value-type uploads derive fields from their type without querying an instance provider.</summary>
    /// <param name="enabled">Whether a compiled diagnostic layout is supplied.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructTypeMetadataRemainsCompatible(bool enabled)
    {
        using var storage = new MappedBuffer();
        var value = new TypeData { Number = 73 };
        var metadata = new IShaderReferenceMetadata?[1];
        ShaderDataLayout layout = CreateLayout();

        Assert.Empty(StaticFields<TypeData>());
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            Assert.Throws<ArgumentException>(() => layout.ValidateTransferLayout(4, StaticFields<TypeData>(), "UploadNumber"));
        }

        ShaderDataUpload.CopyFrom<TypeData>(new[] { value }, storage, 0, metadata, UnexpectedReference, enabled ? layout : null);

        Assert.Equal(73U, MemoryMarshal.Read<uint>(storage.Bytes));
        Assert.Empty(Assert.IsAssignableFrom<IShaderReferenceMetadata>(metadata[0]).References);
    }

    private static IReadOnlyList<ShaderTransferField> StaticFields<T>()
        where T : IShaderArguments => T.TransferFields;

    private static byte[] UnexpectedReference(ShaderValue value)
        => throw new InvalidOperationException("Numeric-only uploads must not encode references.");

    private static ShaderDataLayout CreateLayout()
    {
        const string Reflection = """
            {
              "lumyteAbi": 1,
              "parameters": [{
                "name": "__lumyte_schema_UploadNumber",
                "type": { "resultType": { "kind": "pointer", "valueType": {
                  "kind": "struct", "name": "UploadNumber",
                  "sizes": [{ "kind": "uniform", "value": 4 }],
                  "fields": [{
                    "name": "Number",
                    "type": { "kind": "scalar", "scalarType": "uint32" },
                    "binding": { "offset": 0, "size": 4 }
                  }]
                } } }
              }]
            }
            """;
        var target = new ShaderTargetData(ShaderTarget.SpirV, ShaderStage.Compute, "main", "test", "row-major", [], Reflection);
        return ShaderDataLayout.Data(target, "UploadNumber");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TypeData : IShaderArguments
    {
        public uint Number;

        public static string ShaderTypeName => "UploadNumber";

        public static int TransferSize => sizeof(uint);

        public readonly IReadOnlyList<ShaderTransferField> GetTransferFields()
            => throw new InvalidOperationException("Value-type diagnostics must not query an instance provider.");

        public readonly void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in Number, 1)));
    }

    private sealed class InstanceData : IShaderArguments
    {
        public static string ShaderTypeName => "UploadNumber";

        public static int TransferSize => sizeof(uint);

        public uint Number { get; init; }

        public int Probes { get; private set; }

        public IReadOnlyList<ShaderTransferField> GetTransferFields()
        {
            Probes++;
            return [new("Number", 0, sizeof(uint), typeof(uint))];
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            uint number = Number;
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in number, 1)));
        }
    }

    private sealed class MappedBuffer : IGraphicsBuffer<byte>
    {
        public byte[] Bytes { get; } = new byte[sizeof(uint)];

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
            source[..checked((int)length)].CopyTo(Bytes.AsSpan(checked((int)offset)));
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
