namespace Lumyte.Graphics;

// Command recording and submission; owns GPU copy commands and command scopes.

/// <summary>
/// Provides command recording, submission, and completion independently of buffer CPU access.
/// </summary>
internal interface ICommandBufferBackendContract
{
    /// <summary>
    /// Creates a native command recording scope.
    /// </summary>
    /// <returns>The owned command recording scope.</returns>
    object CreateCommandEncoder();

    /// <summary>
    /// Submits the command buffer exactly once.
    /// </summary>
    /// <param name="commands">The finished, unsubmitted command buffer.</param>
    /// <returns>The completion handle for the explicitly submitted work.</returns>
    object Submit(CommandBuffer commands);

    /// <summary>
    /// Begins a scoped render pass on the encoder handle.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The active render recording scope, which must end before more parent commands.</returns>
    object BeginRenderPass(object handle, RenderPassDesc desc);

    /// <summary>
    /// Records a compute invocation with validated pipeline and arguments.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="pipeline">The matching pipeline from the same device.</param>
    /// <param name="arguments">The leased arguments created for the selected pipeline.</param>
    /// <param name="x">The X workgroup count.</param>
    /// <param name="y">The Y workgroup count.</param>
    /// <param name="z">The Z workgroup count.</param>
    void Dispatch(object handle, ComputePipeline pipeline, ShaderArguments arguments, uint x, uint y, uint z);

    /// <summary>
    /// Records a byte-range GPU copy, retaining both allocations.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="source">The caller-owned data or GPU source range.</param>
    /// <param name="destination">The caller-owned destination storage or GPU range.</param>
    void RecordCopyBuffer(object handle, BufferRange source, BufferRange destination);

    /// <summary>
    /// Records an explicit image upload with aligned buffer row layout.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="source">The caller-owned data or GPU source range.</param>
    /// <param name="destination">The caller-owned destination storage or GPU range.</param>
    /// <param name="bytesPerRow">The buffer row pitch in bytes, a multiple of 256 with sufficient row storage.</param>
    void RecordCopyBufferToTexture(object handle, BufferRange source, IGraphicsTexture destination, uint bytesPerRow);

    /// <summary>
    /// Records an explicit image readback into caller-owned storage.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="source">The caller-owned data or GPU source range.</param>
    /// <param name="destination">The caller-owned destination storage or GPU range.</param>
    /// <param name="bytesPerRow">The buffer row pitch in bytes, a multiple of 256 with sufficient row storage.</param>
    void RecordCopyTextureToBuffer(object handle, IGraphicsTexture source, BufferRange destination, uint bytesPerRow);

    /// <summary>
    /// Finishes an encoder and transfers its leases to the command buffer.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <returns>The owned, single-use finished command buffer.</returns>
    object Finish(object handle);

    /// <summary>
    /// Selects a validated graphics pipeline for an active pass.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="pipeline">The matching pipeline from the same device.</param>
    void SetPipeline(object handle, GraphicsPipeline pipeline);

    /// <summary>
    /// Records a validated viewport.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="viewport">The viewport contained in the target image.</param>
    void SetViewport(object handle, Viewport viewport);

    /// <summary>
    /// Records a validated scissor rectangle.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="scissor">The scissor rectangle contained in the target image.</param>
    void SetScissor(object handle, Scissor scissor);

    /// <summary>
    /// Records a validated index-buffer range.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="indices">The non-owning typed index-buffer range.</param>
    /// <param name="format">The representation matching the index element type.</param>
    void SetIndexBuffer(object handle, BufferRange indices, IndexFormat format);

    /// <summary>
    /// Records a non-indexed draw.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    void Draw(object handle, DrawDesc desc);

    /// <summary>
    /// Binds material arguments and records a non-indexed draw.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="arguments">The leased arguments created for the selected pipeline.</param>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    void DrawWithArguments(object handle, ShaderArguments arguments, DrawDesc desc);

    /// <summary>
    /// Records an indexed draw.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    void DrawIndexed(object handle, IndexedDrawDesc desc);

    /// <summary>
    /// Ends an active render pass.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    void End(object handle);

    /// <summary>
    /// Reports completion and releases submitted leases once observed.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <returns>True when completion has been observed; otherwise false.</returns>
    bool IsCompleted(object handle);

    /// <summary>
    /// Polls completion with cancellation and error propagation.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="cancellationToken">Cancels observation without cancelling the submitted GPU work.</param>
    void Wait(object handle, CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously polls completion with cancellation and error propagation.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="cancellationToken">Cancels observation without cancelling the submitted GPU work.</param>
    /// <returns>A task that completes when GPU completion is observed or cancellation is requested.</returns>
    ValueTask WaitAsync(object handle, CancellationToken cancellationToken);
}
