using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal static class VulkanShaderTransfer
{
    internal static ShaderRootArguments CreateRoot<T>(VulkanDevice owner, in T value, Func<int, IShaderRootStorage> factory, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : IShaderArguments
    {
        if (!typeof(T).IsValueType)
        {
            ArgumentNullException.ThrowIfNull(value);
        }

        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        int size = T.TransferSize;
        IShaderRootStorage storage = ShaderRootArguments.Allocate(root, name, size, factory);
        try
        {
            var writer = new VulkanShaderValueWriter(owner, size, storage, null, 0, diagnostic);
            value.Write(ref writer);
            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields(in value), name);
            return ShaderRootArguments.Create(root, name, size, storage, references, prepare);
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    internal static ShaderRootArguments CreateUnmanaged<T>(VulkanDevice owner, in T value, string rootParameter, string? shaderTypeName, Func<int, IShaderRootStorage> factory, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        IShaderRootStorage storage = ShaderRootArguments.Allocate(rootParameter, name, size, factory);
        try
        {
            var writer = new VulkanShaderValueWriter(owner, size, storage, null, 0, diagnostic);
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
            IShaderReferenceMetadata references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields<T>(), name);
            return ShaderRootArguments.Create(rootParameter, name, size, storage, references, prepare);
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    internal static void CopyFrom<T>(VulkanDevice owner, ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, IShaderReferenceMetadata?[] metadata, ShaderDataLayout? diagnostic)
        where T : IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(metadata);
        int stride = T.TransferSize;
        if (stride <= 0 || elementOffset > (ulong)metadata.Length || (ulong)source.Length > (ulong)metadata.Length - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        if (source.IsEmpty)
        {
            return;
        }

        int start = checked((int)elementOffset);
        Array.Clear(metadata, start, source.Length);
        try
        {
            ulong offset = checked(elementOffset * (ulong)stride);
            int length = checked(source.Length * stride);
            storage.ValidateByteRange(offset, (ulong)length);
            if (!storage.IsMapped || storage.Memory != MemoryPreference.Upload)
            {
                throw new InvalidOperationException("CopyFrom requires idle mapped upload storage.");
            }

            for (int i = 0; i < source.Length; i++)
            {
                if (!typeof(T).IsValueType)
                {
                    ArgumentNullException.ThrowIfNull(source[i]);
                }

                var writer = new VulkanShaderValueWriter(owner, stride, null, storage, checked(offset + ((ulong)i * (ulong)stride)), diagnostic);
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

    private static void DisposeFailed(IShaderRootStorage storage)
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
