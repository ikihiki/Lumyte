using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal static class WgpuShaderTransfer
{
    internal static WgpuRootStorage CreateRoot<T>(WgpuDevice owner, in T value)
        where T : struct, IShaderArguments
    {
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        int size = T.TransferSize;
        int referenceCount = T.ReferenceCount;
        WgpuShaderValueWriter.ValidateExpectedCount(size, referenceCount);
        var storage = new WgpuRootStorage(owner, size);
        try
        {
            var writer = new WgpuShaderValueWriter(owner, size, storage, default, referenceCount);
            value.Write(ref writer);
            writer.Complete();
            storage.Initialize(ShaderRootArguments.Create(root, name, size, null), writer.Table);
            return storage;
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    internal static WgpuRootStorage CreateUnmanaged<T>(WgpuDevice owner, in T value, string rootParameter, string? shaderTypeName)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        WgpuShaderValueWriter.ValidateExpectedCount(size, 0);
        var storage = new WgpuRootStorage(owner, size);
        try
        {
            var writer = new WgpuShaderValueWriter(owner, size, storage, default, 0);
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
            writer.Complete();
            storage.Initialize(ShaderRootArguments.Create(rootParameter, name, size, null), writer.Table);
            return storage;
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    internal static void CopyFrom<T>(WgpuDevice owner, ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, ulong count)
        where T : struct, IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(storage);
        int stride = T.TransferSize;
        if (stride <= 0 || elementOffset > count || (ulong)source.Length > count - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        {
            ulong offset = checked(elementOffset * (ulong)stride);
            int length = checked(source.Length * stride);

            int referenceCount = T.ReferenceCount;
            WgpuShaderValueWriter.ValidateExpectedCount(stride, referenceCount);
            if (storage is not WgpuBuffer<byte> buffer || !ReferenceEquals(buffer.Owner, owner))
            {
                throw new ArgumentException("Upload storage belongs to another backend or device.", nameof(storage));
            }

            if (source.IsEmpty)
            {
                if (!storage.IsMapped || storage.Memory != MemoryPreference.Upload)
                {
                    throw new InvalidOperationException("CopyFrom requires idle mapped upload storage.");
                }

                return;
            }

            Span<byte> upload = buffer.GetMappedUploadSpan(offset, length);

            for (int i = 0; i < source.Length; i++)
            {
                var writer = new WgpuShaderValueWriter(owner, stride, null, upload.Slice(checked(i * stride), stride), referenceCount);
                source[i].Write(ref writer);
                writer.Complete();
            }
        }
    }

    private static void DisposeFailed(WgpuRootStorage storage)
    {
        try
        {
            storage.Dispose();
        }
        catch
        {
            // Preserve the original transfer or selection failure.
        }
    }
}
