using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal static class VulkanShaderTransfer
{
    internal static ShaderRootArguments CreateRoot<T>(VulkanDevice owner, in T value, Span<byte> destination, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : struct, IShaderArguments
    {
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        int size = T.TransferSize;
        VulkanPushConstants.ValidateSize(owner, size);
        var writer = new VulkanShaderValueWriter(owner, size, destination, T.ReferenceCount, diagnostic);
        value.Write(ref writer);
        IShaderReferenceMetadata references = writer.Complete();
        diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields(in value), name);
        return ShaderRootArguments.Create(root, name, size, references, prepare);
    }

    internal static ShaderRootArguments CreateUnmanaged<T>(VulkanDevice owner, in T value, string rootParameter, string? shaderTypeName, Span<byte> destination, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        VulkanPushConstants.ValidateSize(owner, size);
        var writer = new VulkanShaderValueWriter(owner, size, destination, 0, diagnostic);
        writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
        IShaderReferenceMetadata references = writer.Complete();
        diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields<T>(), name);
        return ShaderRootArguments.Create(rootParameter, name, size, references, prepare);
    }

    internal static void CopyFrom<T>(VulkanDevice owner, ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, IShaderReferenceMetadata?[] metadata, ShaderDataLayout? diagnostic)
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
            VulkanShaderValueWriter.ValidateExpectedCount(stride, referenceCount);

            if (source.IsEmpty)
            {
                return;
            }

            if (storage is not VulkanBuffer<byte> native)
            {
                throw new ArgumentException("Shader upload requires native backend storage.", nameof(storage));
            }

            Span<byte> destination = native.GetMappedUploadSpan(offset, length);
            using GraphicsValidation.HostWriteLease? lease = owner.Validation?.BeginHostWrite(storage, offset, (ulong)length);

            for (int i = 0; i < source.Length; i++)
            {
                var writer = new VulkanShaderValueWriter(owner, stride, destination.Slice(checked(i * stride), stride), referenceCount, diagnostic);
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
}
