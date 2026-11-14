using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal static class BrowserShaderTransfer
{
    internal static BrowserRootStorage CreateRoot<T>(BrowserDevice owner, in T value, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : struct, IShaderArguments
    {
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        int size = T.TransferSize;
        int referenceCount = T.ReferenceCount;
        BrowserShaderValueWriter.ValidateExpectedCount(size, referenceCount);
        var storage = new BrowserRootStorage(owner, size);
        try
        {
            var writer = new BrowserShaderValueWriter(owner, size, storage, null, 0, referenceCount, diagnostic);
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

    internal static BrowserRootStorage CreateUnmanaged<T>(BrowserDevice owner, in T value, string rootParameter, string? shaderTypeName, ShaderDataLayout? diagnostic = null, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
        where T : unmanaged
    {
        string name = shaderTypeName ?? ShaderTransfer.TypeName<T>();
        int size = Unsafe.SizeOf<T>();
        BrowserShaderValueWriter.ValidateExpectedCount(size, 0);
        var storage = new BrowserRootStorage(owner, size);
        try
        {
            var writer = new BrowserShaderValueWriter(owner, size, storage, null, 0, 0, diagnostic);
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

    internal static void CopyFrom<T>(BrowserDevice owner, ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, IShaderReferenceMetadata?[] metadata, ShaderDataLayout? diagnostic)
        where T : struct, IShaderArguments
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
        JSObject? upload = null;
        try
        {
            ulong offset = checked(elementOffset * (ulong)stride);
            int length = checked(source.Length * stride);

            int referenceCount = T.ReferenceCount;
            BrowserShaderValueWriter.ValidateExpectedCount(stride, referenceCount);
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
            using GraphicsValidation.HostWriteLease? lease = owner.Validation?.BeginHostWrite(storage, offset, (ulong)length);

            for (int i = 0; i < source.Length; i++)
            {
                var writer = new BrowserShaderValueWriter(owner, stride, null, upload, checked(i * stride), referenceCount, diagnostic);
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
