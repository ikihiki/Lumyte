using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal static class Direct3D12ShaderTransfer
{
    internal static (ShaderRootArguments Root, Direct3D12ArgumentTable? Table) CreateRoot<T>(Direct3D12Device owner, in T value, Span<byte> destination)
        where T : struct, IShaderArguments
    {
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        int size = T.TransferSize;
        ValidateRootSize(size);
        var writer = new Direct3D12ShaderValueWriter(owner, size, destination, T.ReferenceCount);
        value.Write(ref writer);
        writer.Complete();
        return (ShaderRootArguments.Create(root, name, size, null), writer.Table);
    }

    internal static ShaderRootArguments CreateUnmanaged<T>(Direct3D12Device owner, in T value, string rootParameter, string? shaderTypeName, Span<byte> destination)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        ValidateRootSize(size);
        var writer = new Direct3D12ShaderValueWriter(owner, size, destination, 0);
        writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
        writer.Complete();
        return ShaderRootArguments.Create(rootParameter, name, size, null);
    }

    internal static void CopyFrom<T>(Direct3D12Device owner, ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, ulong count)
        where T : struct, IShaderArguments
    {
        if (elementOffset > count || (ulong)source.Length > count - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        int stride = T.TransferSize;
        if (stride <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        int referenceCount = T.ReferenceCount;
        Direct3D12ShaderValueWriter.ValidateExpectedCount(stride, referenceCount);
        ulong offset = checked(elementOffset * (ulong)stride);
        int length = checked(source.Length * stride);
        if (storage is not Direct3D12Buffer<byte> native)
        {
            throw new ArgumentException("Shader upload requires native backend storage.", nameof(storage));
        }

        Span<byte> destination = native.GetMappedUploadSpan(offset, length);
        for (int i = 0; i < source.Length; i++)
        {
            var writer = new Direct3D12ShaderValueWriter(owner, stride, destination.Slice(checked(i * stride), stride), referenceCount);
            source[i].Write(ref writer);
            writer.Complete();
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
