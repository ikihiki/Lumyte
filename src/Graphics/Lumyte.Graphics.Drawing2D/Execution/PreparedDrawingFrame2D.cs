using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Owns one retained frame until confirmed execution and command-memory retirement.</summary>
/// <remarks>No scene lease is retained. Unknown submission outcomes keep the frame and its slot quarantined.
/// Dispose never waits and never releases possibly executing resources.</remarks>
public sealed class PreparedDrawingFrame2D : IDisposable
{
    private readonly IGraphicDevice _device;
    private readonly DrawingProfileContext? _profileContext;
    private readonly RetainedDrawingCache2D? _cache;
    private PreparedDraw2D? _drawing;
    private Action<bool>? _release;
    private IGraphicsCommandBuffer? _commands;
    private IGraphicsSubmission? _submission;
    private bool _released;
    private bool _recordSucceeded;

    internal PreparedDrawingFrame2D(IGraphicDevice device, RenderTarget2D target, IReadOnlyList<IPreparedCommandBatch2D> batches, Action<bool> release, DrawingProfileContext? profileContext = null, RetainedDrawingCache2D? cache = null)
    {
        _device = device;
        _profileContext = profileContext ?? default(DrawingProfileContext);
        _cache = cache;
        _drawing = new(target, batches);
        _release = release;
    }

    /// <summary>Records this frame exactly once without submitting it.</summary>
    /// <param name="commands">The caller-owned command memory, retained until retirement.</param>
    public void Record(IGraphicsCommandBuffer commands)
    {
        using var profile = DrawingProfileScope.Start(10, _profileContext, root: true);
        try
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
            _drawing!.RecordCore(commands, profile);
            _recordSucceeded = true;

            profile?.Success();
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
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
        using var profile = DrawingProfileScope.Start(12, _profileContext, root: true);
        profile?.ReleaseOperation(1);
        try
        {
            ObjectDisposedException.ThrowIf(_released, this);
            if (_submission?.Status != SubmissionStatus.Completed || _commands?.State != CommandBufferState.Disposed || _device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("Retirement requires confirmed completion, disposed commands, and a ready device; uncertain work remains quarantined.");
            }

            Release(true);

            profile?.Success();
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
        finally
        {
            if (_cache is not null && profile is { CountersEnabled: true })
            {
                profile.Snapshot(_cache.Statistics);
            }
        }
    }

    /// <summary>Explicitly asserts that recorded command memory was never submitted and has already been disposed.</summary>
    /// <remarks>Do not call after a throwing Submit whose acceptance is unknown.</remarks>
    public void AbortUnsubmitted()
    {
        using var profile = DrawingProfileScope.Start(12, _profileContext, root: true);
        profile?.ReleaseOperation(2);
        try
        {
            ObjectDisposedException.ThrowIf(_released, this);
            if (_submission is not null || _commands?.State != CommandBufferState.Disposed || _device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("Abort requires disposed, explicitly never-submitted command memory and a ready device.");
            }

            Release(false);

            profile?.Success();
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
        finally
        {
            if (_cache is not null && profile is { CountersEnabled: true })
            {
                profile.Snapshot(_cache.Statistics);
            }
        }
    }

    /// <summary>Releases unrecorded work; recorded work requires explicit Retire or AbortUnsubmitted.</summary>
    public void Dispose()
    {
        using var profile = DrawingProfileScope.Start(12, _profileContext, root: true);
        profile?.ReleaseOperation(3);
        try
        {
            if (_released)
            {
                profile?.Skip();
                return;
            }

            if (_commands is not null)
            {
                throw new InvalidOperationException("Recorded frames must be explicitly retired or asserted unsubmitted; resources remain quarantined.");
            }

            Release(false);

            profile?.Success();
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
        finally
        {
            if (_cache is not null && profile is { CountersEnabled: true })
            {
                profile.Snapshot(_cache.Statistics);
            }
        }
    }

    // Only the application presenter may substitute confirmed device-generation retirement.
    // Commands must be released first, and residency must never be published on this path.
    internal void ReleaseAfterDeviceRetirement()
    {
        using var profile = DrawingProfileScope.Start(12, _profileContext, root: true);
        profile?.ReleaseOperation(4);
        try
        {
            if (_released)
            {
                profile?.Skip();
                return;
            }

            if (_commands is not null && _commands.State != CommandBufferState.Disposed)
            {
                throw new InvalidOperationException("Dispose command memory after confirmed device retirement first.");
            }

            Release(false);

            profile?.Success();
        }
        catch (Exception error)
        {
            profile?.Failure(error);
            throw;
        }
        finally
        {
            if (_cache is not null && profile is { CountersEnabled: true })
            {
                profile.Snapshot(_cache.Statistics);
            }
        }
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
