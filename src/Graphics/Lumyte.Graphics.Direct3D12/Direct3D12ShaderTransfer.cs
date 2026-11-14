using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal static class Direct3D12ShaderTransfer
{
    // The caller owns the exact DWORD-rounded stack span until commands_root_constants returns.
    internal static ShaderRootArguments CreateRoot<T>(Direct3D12Device owner, in T value, Span<byte> payload, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : struct, IShaderArguments
    {
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        int size = T.TransferSize;
        ValidateRootSize(size);
        var writer = new Direct3D12ShaderValueWriter(owner, size, payload, T.ReferenceCount, diagnostic);
        value.Write(ref writer);
        IShaderReferenceMetadata references = writer.Complete();
        diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields(in value), name);
        return ShaderRootArguments.Create(root, name, size, references, prepare);
    }

    internal static ShaderRootArguments CreateUnmanaged<T>(Direct3D12Device owner, in T value, string rootParameter, string? shaderTypeName, Span<byte> payload, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        ValidateRootSize(size);
        var writer = new Direct3D12ShaderValueWriter(owner, size, payload, 0, diagnostic);
        writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
        IShaderReferenceMetadata references = writer.Complete();
        diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields<T>(), name);
        return ShaderRootArguments.Create(rootParameter, name, size, references, prepare);
    }

    internal static void CopyFrom<T>(Direct3D12Device owner, ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, IShaderReferenceMetadata?[] metadata, ShaderDataLayout? diagnostic)
        where T : struct, IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(metadata);
        if (elementOffset > (ulong)metadata.Length || (ulong)source.Length > (ulong)metadata.Length - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        int start = checked((int)elementOffset);
        Array.Clear(metadata, start, source.Length);
        try
        {
            int stride = T.TransferSize;
            if (stride <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(elementOffset));
            }

            ulong offset = checked(elementOffset * (ulong)stride);
            int length = checked(source.Length * stride);

            int referenceCount = T.ReferenceCount;
            Direct3D12ShaderValueWriter.ValidateExpectedCount(stride, referenceCount);

            if (source.IsEmpty)
            {
                return;
            }

            if (storage is not Direct3D12Buffer<byte> native)
            {
                throw new ArgumentException("Shader upload requires native backend storage.", nameof(storage));
            }

            Span<byte> destination = native.GetMappedUploadSpan(offset, length);
            using GraphicsValidation.HostWriteLease? lease = owner.Validation?.BeginHostWrite(storage, offset, (ulong)length);

            for (int i = 0; i < source.Length; i++)
            {
                var writer = new Direct3D12ShaderValueWriter(owner, stride, destination.Slice(checked(i * stride), stride), referenceCount, diagnostic);
                source[i].Write(ref writer);
                IShaderReferenceMetadata references = writer.Complete();
                diagnostic?.ValidateTransferLayout(stride, ShaderTransfer.Fields(in source[i]), T.ShaderTypeName);
                metadata[start + i] = references;
            }
        }
        catch
        {
            Array.Clear(metadata, start, source.Length);
            throw;
        }
    }

    internal static int ValidateRootSize(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        int words = checked((int)(((long)size + 3) / 4));
        if (words > Direct3D12Device.RootConstantDwordCapacity)
        {
            throw new NotSupportedException("Application roots support at most 58 DWORDs (232 wire bytes).");
        }

        return words;
    }
}
