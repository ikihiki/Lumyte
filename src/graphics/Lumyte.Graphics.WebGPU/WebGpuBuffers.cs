using Silk.NET.WebGPU;

using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Lumyte.Graphics.WebGPU;

public sealed unsafe partial class WebGpuDevice
{
    public GpuBufferHandle CreateBuffer(GpuBufferDescription description)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        description.Validate();
        WgpuBuffer* native = CreateNativeBuffer(description.Size, ToWebGpuUsage(description.Usage));
        var handle = new GpuBufferHandle(nextBufferId++, description.Size);
        buffers.Add(handle.Value, new((nint)native, description));
        return handle;
    }

    public void WriteBuffer(GpuBufferHandle buffer, ReadOnlySpan<byte> source)
        => WriteBuffer(buffer, 0, source);

    public void WriteBuffer(
        GpuBufferHandle buffer,
        ulong destinationOffset,
        ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        BufferRecord record = RequireBuffer(buffer);
        if ((record.Description.Usage & GpuBufferUsage.CopyDestination) == 0)
        {
            throw new ArgumentException("Buffer does not support copy destination usage.", nameof(buffer));
        }
        if (source.IsEmpty
            || (destinationOffset & 3) != 0
            || (source.Length & 3) != 0
            || destinationOffset > buffer.Size
            || checked((ulong)source.Length) > buffer.Size - destinationOffset)
        {
            throw new ArgumentException(
                "The destination and source must be non-empty, four-byte aligned, and fit the buffer.",
                nameof(source));
        }
        fixed (byte* bytes = source)
        {
            api.QueueWriteBuffer(
                queue,
                (WgpuBuffer*)record.Handle,
                destinationOffset,
                bytes,
                checked((nuint)source.Length));
        }
    }

    public byte[] ReadBuffer(GpuBufferHandle buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        BufferRecord record = RequireBuffer(buffer);
        if ((record.Description.Usage & GpuBufferUsage.CopySource) == 0)
        {
            throw new ArgumentException("Buffer does not support copy source usage.", nameof(buffer));
        }
        WgpuBuffer* readback = CreateNativeBuffer(buffer.Size, BufferUsage.CopyDst | BufferUsage.MapRead);
        try
        {
            SubmitCopy((WgpuBuffer*)record.Handle, readback, buffer.Size);
            return MapReadback(readback, checked((nuint)buffer.Size));
        }
        finally
        {
            api.BufferRelease(readback);
        }
    }

    public GpuBufferView CreateBufferView(
        GpuBufferHandle buffer,
        GpuBufferViewDescription description)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        BufferRecord record = RequireBuffer(buffer);
        if ((record.Description.Usage & GpuBufferUsage.ShaderData) == 0)
        {
            throw new ArgumentException("Buffer views require ShaderData usage.", nameof(buffer));
        }
        GpuBufferViewDescription normalized = description.Normalize(buffer);
        var view = new GpuBufferView(new(nextResourceId++), buffer, normalized);
        bufferViews.Add(view.Id.Value, view);
        return view;
    }

    public void DestroyBufferView(GpuBufferView view)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!bufferViews.Remove(view.Id.Value, out GpuBufferView registered) || registered != view)
        {
            if (!registered.Id.IsNull) { bufferViews.Add(registered.Id.Value, registered); }
            throw new ArgumentException("Buffer view does not belong to this WebGPU device.", nameof(view));
        }
        InvalidateBindGroups();
    }

    public void DestroyBuffer(GpuBufferHandle buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        BufferRecord record = RequireBuffer(buffer);
        if (bufferViews.Values.Any(view => view.Buffer == buffer))
        {
            throw new InvalidOperationException("Buffer still has a live view.");
        }
        buffers.Remove(buffer.Value);
        InvalidateBindGroups();
        api.BufferRelease((WgpuBuffer*)record.Handle);
    }

    private BufferRecord RequireBuffer(GpuBufferHandle buffer)
    {
        if (!buffers.TryGetValue(buffer.Value, out BufferRecord? record)
            || record.Description.Size != buffer.Size)
        {
            throw new ArgumentException("Buffer does not belong to this WebGPU device.", nameof(buffer));
        }
        return record;
    }

    private static BufferUsage ToWebGpuUsage(GpuBufferUsage usage)
    {
        BufferUsage result = 0;
        if ((usage & GpuBufferUsage.CopySource) != 0) { result |= BufferUsage.CopySrc; }
        if ((usage & GpuBufferUsage.CopyDestination) != 0) { result |= BufferUsage.CopyDst; }
        if ((usage & GpuBufferUsage.ShaderData) != 0) { result |= BufferUsage.Storage; }
        if ((usage & GpuBufferUsage.IndirectArguments) != 0) { result |= BufferUsage.Indirect; }
        return result;
    }
}
