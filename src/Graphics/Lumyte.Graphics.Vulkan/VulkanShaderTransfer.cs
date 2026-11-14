using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal static class VulkanShaderTransfer
{
    internal static (ShaderRootArguments Root, VulkanArgumentTable? Table) CreateRoot<T>(VulkanDevice owner, in T value, Span<byte> destination, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : struct, IShaderArguments
    {
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        int size = T.TransferSize;
        VulkanPushConstants.ValidateSize(owner, size);
        var writer = new VulkanShaderValueWriter(owner, size, destination, T.ReferenceCount, diagnostic, collect: owner.Validation != null || prepare != null);
        value.Write(ref writer);
        IShaderReferenceMetadata? references = writer.Complete();
        diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields(in value), name);
        return (ShaderRootArguments.Create(root, name, size, references, prepare), writer.Table);
    }

    internal static ShaderRootArguments CreateUnmanaged<T>(VulkanDevice owner, in T value, string rootParameter, string? shaderTypeName, Span<byte> destination, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        VulkanPushConstants.ValidateSize(owner, size);
        var writer = new VulkanShaderValueWriter(owner, size, destination, 0, diagnostic, collect: owner.Validation != null || prepare != null);
        writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
        IShaderReferenceMetadata? references = writer.Complete();
        diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields<T>(), name);
        return ShaderRootArguments.Create(rootParameter, name, size, references, prepare);
    }

    internal static void CopyFrom<T>(VulkanDevice owner, ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, ulong count, HashSet<IShaderReference>? collector, ShaderDataLayout? diagnostic)
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
        VulkanShaderValueWriter.ValidateExpectedCount(stride, referenceCount);
        ulong offset = checked(elementOffset * (ulong)stride);
        int length = checked(source.Length * stride);
        if (storage is not VulkanBuffer<byte> native)
        {
            throw new ArgumentException("Shader upload requires native backend storage.", nameof(storage));
        }

        Span<byte> destination = native.GetMappedUploadSpan(offset, length);
        using GraphicsValidation.HostWriteLease? lease = owner.Validation?.BeginHostWrite(storage, offset, (ulong)length);
        for (int i = 0; i < source.Length; i++)
        {
            var writer = new VulkanShaderValueWriter(owner, stride, destination.Slice(checked(i * stride), stride), referenceCount, diagnostic, collector: collector);
            source[i].Write(ref writer);
            writer.Complete();
            diagnostic?.ValidateTransferLayout(stride, ShaderTransfer.Fields(in source[i]), T.ShaderTypeName);
        }
    }
}
