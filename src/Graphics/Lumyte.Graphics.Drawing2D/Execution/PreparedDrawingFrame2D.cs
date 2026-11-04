using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Owns one retained frame until confirmed execution and command-memory retirement.</summary>
/// <remarks>No scene lease is retained. Unknown submission outcomes keep the frame and its slot quarantined.
/// Dispose never waits and never releases possibly executing resources.</remarks>
public sealed class PreparedDrawingFrame2D : IDisposable
{
    private readonly IGraphicDevice _device;
    private PreparedDraw2D? _drawing;
    private Action<bool>? _release;
    private IGraphicsCommandBuffer? _commands;
    private IGraphicsSubmission? _submission;
    private bool _released;
    private bool _recordSucceeded;

    internal PreparedDrawingFrame2D(IGraphicDevice device, RenderTarget2D target, IReadOnlyList<IPreparedCommandBatch2D> batches, Action<bool> release)
    {
        _device = device;
        _drawing = new(target, batches);
        _release = release;
    }

    /// <summary>Records this frame exactly once without submitting it.</summary>
    /// <param name="commands">The caller-owned command memory, retained until retirement.</param>
    public void Record(IGraphicsCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ObjectDisposedException.ThrowIf(_released, this);
        if (_commands is not null)
        {
            throw new InvalidOperationException("A retained frame can be recorded only once.");
        }

        if (_device.Status != GraphicsDeviceStatus.Ready || commands.State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("Recording requires a ready device and recording command buffer.");
        }

        _commands = commands;
        _drawing!.Record(commands);
        _recordSucceeded = true;
    }

    /// <summary>Attaches the exact submission returned by the caller's successful queue submission.</summary>
    /// <param name="actualSubmit">The caller-owned completion tracker for this frame's commands.</param>
    public void AttachSubmission(IGraphicsSubmission actualSubmit)
    {
        ArgumentNullException.ThrowIfNull(actualSubmit);
        ObjectDisposedException.ThrowIf(_released, this);
        if (!_recordSucceeded || _commands is null || _submission is not null)
        {
            throw new InvalidOperationException("Record once before attaching exactly one submission.");
        }

        if (_commands.State is not (CommandBufferState.Submitted or CommandBufferState.Completed or CommandBufferState.Disposed))
        {
            throw new InvalidOperationException("Commands must have been successfully submitted.");
        }

        _submission = actualSubmit;
    }

    /// <summary>Publishes residency and releases the slot only after completion, command disposal, and device readiness.</summary>
    public void Retire()
    {
        ObjectDisposedException.ThrowIf(_released, this);
        if (_submission?.Status != SubmissionStatus.Completed || _commands?.State != CommandBufferState.Disposed || _device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("Retirement requires confirmed completion, disposed commands, and a ready device; uncertain work remains quarantined.");
        }

        Release(true);
    }

    /// <summary>Explicitly asserts that recorded command memory was never submitted and has already been disposed.</summary>
    /// <remarks>Do not call after a throwing Submit whose acceptance is unknown.</remarks>
    public void AbortUnsubmitted()
    {
        ObjectDisposedException.ThrowIf(_released, this);
        if (_submission is not null || _commands?.State != CommandBufferState.Disposed || _device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("Abort requires disposed, explicitly never-submitted command memory and a ready device.");
        }

        Release(false);
    }

    /// <summary>Releases unrecorded work; recorded work requires explicit Retire or AbortUnsubmitted.</summary>
    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        if (_commands is not null)
        {
            throw new InvalidOperationException("Recorded frames must be explicitly retired or asserted unsubmitted; resources remain quarantined.");
        }

        Release(false);
    }

    private void Release(bool completed)
    {
        _release!(completed);
        _release = null;
        _released = true;
        _drawing = null;
        _commands = null;
        _submission = null;
    }
}
