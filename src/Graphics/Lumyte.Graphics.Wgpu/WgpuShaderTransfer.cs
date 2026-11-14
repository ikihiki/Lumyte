using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal static class WgpuShaderTransfer
{
    internal static WgpuRootStorage CreateRoot<T>(WgpuDevice owner, in T value, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : IShaderArguments
    {
        if (!typeof(T).IsValueType)
        {
            ArgumentNullException.ThrowIfNull(value);
        }

        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        int size = T.TransferSize;
        var storage = new WgpuRootStorage(owner, size);
        try
        {
            var writer = new WgpuShaderValueWriter(owner, size, storage, null, 0, diagnostic);
            value.Write(ref writer);
            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields(in value), name);
            storage.Initialize(ShaderRootArguments.Create(root, name, size, references, prepare));
            return storage;
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    internal static WgpuRootStorage CreateUnmanaged<T>(WgpuDevice owner, in T value, string rootParameter, string? shaderTypeName, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        var storage = new WgpuRootStorage(owner, size);
        try
        {
            var writer = new WgpuShaderValueWriter(owner, size, storage, null, 0, diagnostic);
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields<T>(), name);
            storage.Initialize(ShaderRootArguments.Create(rootParameter, name, size, references, prepare));
            return storage;
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    internal static void CopyFrom<T>(WgpuDevice owner, ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, IShaderReferenceMetadata?[] metadata, ShaderDataLayout? diagnostic)
        where T : IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(metadata);
        int stride = T.TransferSize;
        if (stride <= 0 || elementOffset > (ulong)metadata.Length || (ulong)source.Length > (ulong)metadata.Length - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        int start = checked((int)elementOffset);
        Array.Clear(metadata, start, source.Length);
        try
        {
            if (!storage.IsMapped || storage.Memory != MemoryPreference.Upload)
            {
                throw new InvalidOperationException("CopyFrom requires idle mapped upload storage.");
            }

            if (source.IsEmpty)
            {
                return;
            }

            ulong offset = checked(elementOffset * (ulong)stride);
            int length = checked(source.Length * stride);
            storage.ValidateByteRange(offset, (ulong)length);

            for (int i = 0; i < source.Length; i++)
            {
                if (!typeof(T).IsValueType)
                {
                    ArgumentNullException.ThrowIfNull(source[i]);
                }

                var writer = new WgpuShaderValueWriter(owner, stride, null, storage, checked(offset + ((ulong)i * (ulong)stride)), diagnostic);
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
