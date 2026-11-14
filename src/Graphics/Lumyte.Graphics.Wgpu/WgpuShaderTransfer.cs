using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal static class WgpuShaderTransfer
{
    internal static WgpuRootStorage CreateRoot<T>(WgpuDevice owner, in T value, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
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
            var writer = new WgpuShaderValueWriter(owner, size, storage, default, referenceCount, diagnostic, collect: owner.Validation != null || prepare != null);
            value.Write(ref writer);
            IShaderReferenceMetadata? references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields(in value), name);
            storage.Initialize(ShaderRootArguments.Create(root, name, size, references, prepare), writer.Table);
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
        WgpuShaderValueWriter.ValidateExpectedCount(size, 0);
        var storage = new WgpuRootStorage(owner, size);
        try
        {
            var writer = new WgpuShaderValueWriter(owner, size, storage, default, 0, diagnostic, collect: owner.Validation != null || prepare != null);
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
            IShaderReferenceMetadata? references = writer.Complete();
            diagnostic?.ValidateTransferLayout(size, ShaderTransfer.Fields<T>(), name);
            storage.Initialize(ShaderRootArguments.Create(rootParameter, name, size, references, prepare), writer.Table);
            return storage;
        }
        catch
        {
            DisposeFailed(storage);
            throw;
        }
    }

    internal static void CopyFrom<T>(WgpuDevice owner, ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, ulong count, HashSet<IShaderReference>? collector, ShaderDataLayout? diagnostic)
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
            using GraphicsValidation.HostWriteLease? lease = owner.Validation?.BeginHostWrite(storage, offset, (ulong)length);

            for (int i = 0; i < source.Length; i++)
            {
                var writer = new WgpuShaderValueWriter(owner, stride, null, upload.Slice(checked(i * stride), stride), referenceCount, diagnostic, collector: collector);
                source[i].Write(ref writer);
                writer.Complete();
                diagnostic?.ValidateTransferLayout(stride, ShaderTransfer.Fields(in source[i]), T.ShaderTypeName);
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
