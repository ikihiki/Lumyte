namespace Lumyte.Graphics;

[Flags]
public enum GpuBarrierHazards
{
    None = 0,
    Descriptors = 1 << 0,
    IndirectArguments = 1 << 1,
    DepthCaches = 1 << 2,
}

public readonly record struct GpuTextureCopyFootprint(
    uint Width,
    uint Height,
    uint BytesPerPixel,
    ulong RowPitch)
{
    public ulong RequiredBytes => checked(RowPitch * Height);

    public GpuTextureCopyFootprint Validate()
    {
        if (Width == 0 || Height == 0 || BytesPerPixel == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width));
        }
        if (RowPitch < checked((ulong)Width * BytesPerPixel))
        {
            throw new ArgumentOutOfRangeException(nameof(RowPitch));
        }
        return this;
    }
}

public readonly record struct GpuAliasingResource(
    GpuTextureHandle Texture,
    GpuBufferHandle Buffer)
{
    public static GpuAliasingResource FromTexture(GpuTextureHandle texture) => new(texture, default);
    public static GpuAliasingResource FromBuffer(GpuBufferHandle buffer) => new(default, buffer);
}

public interface IGpuCommandRecorder
{
    void Barrier(GpuStage before, GpuStage after, GpuBarrierHazards hazards);
    void AliasingBarrier(
        GpuAliasingResource beforeResource,
        GpuAliasingResource afterResource,
        GpuStage before,
        GpuStage after,
        GpuBarrierHazards hazards)
        => Barrier(before, after, hazards);
    void BeginRendering(IReadOnlyList<GpuColorAttachment> colors, GpuDepthStencilAttachment? depth);
    void EndRendering();
    void SetPipeline(GpuRasterPipelineHandle pipeline);
    void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor);
    void Draw(uint vertexCount, uint instanceCount);
    void CopyMemoryToTexture(GpuMemoryAddress source, GpuTextureHandle destination, GpuTextureCopyFootprint footprint);
    void CopyTextureToMemory(GpuTextureHandle source, GpuMemoryAddress destination, GpuTextureCopyFootprint footprint);
    void SetResourceTable(GpuResourceTable table);
    void SetRootData(ReadOnlySpan<byte> data);
    void SetComputePipeline(GpuComputePipelineHandle pipeline)
        => throw new NotSupportedException("Compute pipelines are not implemented by this backend.");
    void SetComputeBuffer(uint slot, GpuBufferHandle buffer)
        => throw new NotSupportedException("Compute buffers are not implemented by this backend.");
    void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        => throw new NotSupportedException("Compute dispatch is not implemented by this backend.");
    void End();
}

/// <summary>An explicit, RenderGraph-independent sequence of GPU commands.</summary>
public sealed class GpuCommandBuffer
{
    private readonly IGpuCommandRecorder recorder;
    private bool rendering;
    private bool submitted;

    internal GpuCommandBuffer(IGpuCommandRecorder recorder) =>
        this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));

    public GpuCommandBuffer Barrier(
        GpuStage before,
        GpuStage after,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        VerifyOpen();
        if (rendering) { throw new InvalidOperationException("A barrier cannot be recorded inside rendering."); }
        recorder.Barrier(before, after, hazards);
        return this;
    }

    public GpuCommandBuffer BeginRendering(
        IReadOnlyList<GpuColorAttachment> colorAttachments,
        GpuDepthStencilAttachment? depthStencilAttachment = null)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(colorAttachments);
        if (rendering) { throw new InvalidOperationException("Rendering has already begun."); }
        if (colorAttachments.Count == 0 && depthStencilAttachment is null)
        {
            throw new ArgumentException("Rendering requires an attachment.", nameof(colorAttachments));
        }
        recorder.BeginRendering(colorAttachments.ToArray(), depthStencilAttachment);
        rendering = true;
        return this;
    }

    public GpuCommandBuffer EndRendering()
    {
        VerifyOpen();
        if (!rendering) { throw new InvalidOperationException("Rendering has not begun."); }
        recorder.EndRendering();
        rendering = false;
        return this;
    }

    public GpuCommandBuffer SetPipeline(GpuRasterPipelineHandle pipeline)
    {
        VerifyOpen();
        if (!rendering) { throw new InvalidOperationException("A raster pipeline can only be bound inside rendering."); }
        if (pipeline.IsNull) { throw new ArgumentException("Pipeline cannot be null.", nameof(pipeline)); }
        recorder.SetPipeline(pipeline);
        return this;
    }

    public GpuCommandBuffer SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor)
    {
        VerifyOpen();
        if (!rendering) { throw new InvalidOperationException("Viewport and scissor can only be set inside rendering."); }
        if (viewport.Width <= 0 || viewport.Height <= 0 || scissor.Width == 0 || scissor.Height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport));
        }

        recorder.SetViewportAndScissor(viewport, scissor);
        return this;
    }

    public GpuCommandBuffer Draw(uint vertexCount, uint instanceCount = 1)
    {
        VerifyOpen();
        if (!rendering) { throw new InvalidOperationException("Draw can only be recorded inside rendering."); }
        if (vertexCount == 0 || instanceCount == 0) { throw new ArgumentOutOfRangeException(nameof(vertexCount)); }
        recorder.Draw(vertexCount, instanceCount);
        return this;
    }

    public GpuCommandBuffer CopyTextureToMemory(
        GpuTextureHandle source,
        GpuMemoryAddress destination,
        GpuTextureCopyFootprint footprint)
    {
        VerifyOpen();
        if (rendering) { throw new InvalidOperationException("Copy cannot be recorded inside rendering."); }
        if (source.IsNull) { throw new ArgumentException("Source texture cannot be null.", nameof(source)); }
        if (destination.IsNull) { throw new ArgumentException("Destination address cannot be null.", nameof(destination)); }
        footprint.Validate();
        if (destination.Length < footprint.RequiredBytes)
        {
            throw new ArgumentException("Destination address range is smaller than the copy footprint.", nameof(destination));
        }
        recorder.CopyTextureToMemory(source, destination, footprint);
        return this;
    }

    public GpuCommandBuffer CopyMemoryToTexture(
        GpuMemoryAddress source,
        GpuTextureHandle destination,
        GpuTextureCopyFootprint footprint)
    {
        VerifyOpen();
        if (rendering) { throw new InvalidOperationException("Copy cannot be recorded inside rendering."); }
        if (source.IsNull) { throw new ArgumentException("Source address cannot be null.", nameof(source)); }
        if (destination.IsNull) { throw new ArgumentException("Destination texture cannot be null.", nameof(destination)); }
        footprint.Validate();
        if (source.Length < footprint.RequiredBytes)
        {
            throw new ArgumentException("Source address range is smaller than the copy footprint.", nameof(source));
        }
        recorder.CopyMemoryToTexture(source, destination, footprint);
        return this;
    }

    public GpuCommandBuffer SetResourceTable(GpuResourceTable table)
    {
        VerifyOpen();
        if (!rendering) { throw new InvalidOperationException("A resource table can only be bound inside rendering."); }
        ArgumentNullException.ThrowIfNull(table);
        recorder.SetResourceTable(table);
        return this;
    }

    public GpuCommandBuffer SetRootData(ReadOnlySpan<byte> data)
    {
        VerifyOpen();
        if (!rendering) { throw new InvalidOperationException("Root data can only be set inside rendering."); }
        GpuShaderBindingConvention.ValidateRootData(data);
        recorder.SetRootData(data);
        return this;
    }

    public GpuCommandBuffer SetComputePipeline(GpuComputePipelineHandle pipeline)
    {
        VerifyOpen();
        if (rendering) { throw new InvalidOperationException("A compute pipeline cannot be bound inside rendering."); }
        if (pipeline.IsNull) { throw new ArgumentException("Pipeline cannot be null.", nameof(pipeline)); }
        recorder.SetComputePipeline(pipeline);
        return this;
    }

    public GpuCommandBuffer SetComputeBuffer(uint slot, GpuBufferHandle buffer)
    {
        VerifyOpen();
        if (rendering) { throw new InvalidOperationException("A compute buffer cannot be bound inside rendering."); }
        if (buffer.IsNull) { throw new ArgumentException("Buffer cannot be null.", nameof(buffer)); }
        recorder.SetComputeBuffer(slot, buffer);
        return this;
    }

    public GpuCommandBuffer Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        VerifyOpen();
        if (rendering) { throw new InvalidOperationException("Compute cannot be dispatched inside rendering."); }
        if (groupCountX == 0 || groupCountY == 0 || groupCountZ == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCountX));
        }
        recorder.Dispatch(groupCountX, groupCountY, groupCountZ);
        return this;
    }

    internal IGpuCommandRecorder Finish()
    {
        VerifyOpen();
        if (rendering) { throw new InvalidOperationException("Rendering must be ended before submission."); }
        recorder.End();
        submitted = true;
        return recorder;
    }

    internal IGpuCommandRecorder Recorder => recorder;

    internal void AliasingBarrier(
        GpuAliasingResource beforeResource,
        GpuAliasingResource afterResource,
        GpuStage before,
        GpuStage after,
        GpuBarrierHazards hazards)
    {
        VerifyOpen();
        if (rendering) { throw new InvalidOperationException("An aliasing barrier cannot be recorded inside rendering."); }
        recorder.AliasingBarrier(beforeResource, afterResource, before, after, hazards);
    }

    private void VerifyOpen()
    {
        if (submitted) { throw new InvalidOperationException("Transient command buffer has already been submitted."); }
    }
}

public abstract class GpuSemaphore : IDisposable
{
    public abstract void Dispose();
}

public interface IGpuQueue
{
    GpuCommandBuffer StartCommandRecording();
    GpuSemaphore CreateSemaphore(ulong initialValue = 0);
    void Submit(ReadOnlySpan<GpuCommandBuffer> commandBuffers, GpuSemaphore signalSemaphore, ulong signalValue);
    void Wait(GpuSemaphore semaphore, ulong value);

    /// <summary>
    /// Returns whether the semaphore has reached <paramref name="value"/> without blocking.
    /// Backends should also release command-recording resources for completed submissions.
    /// </summary>
    bool IsComplete(GpuSemaphore semaphore, ulong value) => false;
}
