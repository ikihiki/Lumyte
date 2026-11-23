using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Acquires exclusive graphics readiness plans from the originating build.</summary>
public static class GraphicsPipelineResultExtensions
{
    /// <summary>Acquires one exclusive segmented execution use without submitting work.</summary>
    /// <typeparam name="T">The prepared output type.</typeparam>
    /// <param name="result">The active prepared result.</param>
    /// <param name="pipeline">The exact originating compiled pipeline.</param>
    /// <param name="ownerContext">The caller-owned native context.</param>
    /// <returns>The exclusive explicit execution.</returns>
    public static GraphicsPipelineExecution AcquireExecution<T>(this PipelineResult<T> result, IPipelineIdentity pipeline, IGraphicsOwnerContext ownerContext)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(ownerContext);
        if (result.IsDisposed || result.PlatformState is not GraphicsResultState state)
        {
            throw new ArgumentException("The result is not an active graphics preparation.", nameof(result));
        }

        return state.AcquireExecution(pipeline, ownerContext);
    }

    /// <summary>Acquires one independent record-only use without submitting work.</summary>
    /// <typeparam name="T">The prepared output type.</typeparam>
    /// <param name="result">The active prepared result.</param>
    /// <param name="pipeline">The exact originating compiled pipeline.</param>
    /// <param name="ownerContext">The caller-owned native context.</param>
    /// <returns>The independent exclusive record plan.</returns>
    public static GraphicsPipelineRecordPlan AcquireRecordPlan<T>(this PipelineResult<T> result, IPipelineIdentity pipeline, IGraphicsOwnerContext ownerContext)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(ownerContext);
        if (result.IsDisposed || result.PlatformState is not GraphicsResultState state)
        {
            throw new ArgumentException("The result is not an active graphics preparation.", nameof(result));
        }

        return state.Acquire(pipeline, ownerContext);
    }

    internal static GraphicsPipelineExecution AcquireExecutionUnbound<T>(this PipelineResult<T> result, IPipelineIdentity pipeline)
    {
        if (result.IsDisposed || result.PlatformState is not GraphicsResultState state)
        {
            throw new ArgumentException("The result is not an active graphics preparation.", nameof(result));
        }

        return state.AcquireExecution(pipeline, null);
    }

    internal static void AddRecordValidation<T>(this PipelineResult<T> result, Action validate)
    {
        if (result.IsDisposed || result.PlatformState is not GraphicsResultState state)
        {
            throw new ArgumentException("The result is not an active graphics preparation.", nameof(result));
        }

        state.Run.AddRecordValidation(validate);
    }

    internal static void AddRecordPrelude<T>(this PipelineResult<T> result, Action<StageRecordContext> record)
    {
        if (result.IsDisposed || result.PlatformState is not GraphicsResultState state)
        {
            throw new ArgumentException("The result is not an active graphics preparation.", nameof(result));
        }

        state.Run.Encode(-1, record);
    }
}
