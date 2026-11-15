using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal static class BrowserShaderTransfer
{
    internal static BrowserRootStorage CreateRoot<T>(BrowserDevice owner, in T value)
        where T : struct, IShaderArguments
    {
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        int size = T.TransferSize;
        var storage = new BrowserRootStorage(owner, size);
        try
        {
            var writer = new BrowserShaderValueWriter(owner, size, storage, null, 0);
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

    internal static BrowserRootStorage CreateUnmanaged<T>(BrowserDevice owner, in T value, string rootParameter, string? shaderTypeName)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        var storage = new BrowserRootStorage(owner, size);
        try
        {
            var writer = new BrowserShaderValueWriter(owner, size, storage, null, 0);
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

    internal static void CopyFrom<T>(BrowserDevice owner, ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, ulong count)
        where T : struct, IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(storage);
        int stride = T.TransferSize;
        if (stride <= 0 || elementOffset > count || (ulong)source.Length > count - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        JSObject? upload = null;
        try
        {
            ulong offset = checked(elementOffset * (ulong)stride);
            int length = checked(source.Length * stride);

            if (storage is not BrowserBuffer<byte> buffer || !ReferenceEquals(buffer.Owner, owner))
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

            upload = buffer.AcquireMappedUploadRange(offset, length);

            for (int i = 0; i < source.Length; i++)
            {
                var writer = new BrowserShaderValueWriter(owner, stride, null, upload, checked(i * stride));
                source[i].Write(ref writer);
                writer.Complete();
            }
        }
        finally
        {
            DisposeUploadView(upload);
        }
    }

    private static void DisposeUploadView(JSObject? upload)
    {
        try
        {
            upload?.Dispose();
        }
        catch
        {
            // Releasing a private view must not mask a codec failure or unmap the GPU buffer.
        }
    }

    private static void DisposeFailed(BrowserRootStorage storage)
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
