using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    private long _lastFrame;
    private bool _recordedFrame;

    private void CompleteFrames()
    {
        foreach (Entry entry in _entries.Values.Where(entry => entry.Native is not null).ToArray())
        {
            if (entry.Drawing is { IsCompleted: true } finished)
            {
                bool presented = finished.IsCompletedSuccessfully && finished.Result;
                entry.Root.Source.CompleteFrame(presented);
                entry.Drawing = null;
                if (finished.Exception is { } error)
                {
                    throw new UiApplicationException("presentation-failed", error.InnerException ?? error);
                }
            }
        }

        Driver.CompleteFrames();
    }

    private void AdvanceRendering()
    {
        long now = TimeProvider.GetTimestamp();
        if (State == UiApplicationState.Recovering || (_recordedFrame && TimeProvider.GetElapsedTime(_lastFrame, now) < _options.FrameInterval))
        {
            return;
        }

        bool recorded = false;
        bool busy = false;
        foreach (Entry entry in _entries.Values.Where(entry => entry.Native is not null).ToArray())
        {
            if (entry.Drawing is not null || entry.Stopping || State == UiApplicationState.StopFailed)
            {
                busy |= entry.Drawing is not null && !entry.Stopping;
                continue;
            }

            DrawList2D commands = entry.Root.Source.RecordFrame(GraphicsGeneration);
            entry.Drawing = DrawFrameAsync(entry.Root, commands);
            recorded = true;
        }

        recorded |= Driver.AdvanceFrames();
        if (recorded || !busy)
        {
            double ticks = _options.FrameInterval.TotalSeconds * TimeProvider.TimestampFrequency;
            bool missed = _recordedFrame && TimeProvider.GetElapsedTime(_lastFrame, now).TotalSeconds / _options.FrameInterval.TotalSeconds >= 2;
            _lastFrame = !_recordedFrame || missed || ticks < 1 || ticks > long.MaxValue / 2d
                ? now : unchecked(_lastFrame + (long)Math.Round(ticks));
            _recordedFrame = true;
        }

        foreach (TaskCompletionSource waiter in _frameWaiters)
        {
            waiter.TrySetResult();
        }

        _frameWaiters.Clear();
    }

    private TimeSpan FrameWait()
    {
        TimeSpan remaining = _recordedFrame
            ? _options.FrameInterval - TimeProvider.GetElapsedTime(_lastFrame) : _options.FrameInterval;
        return remaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1)
            : remaining > TimeSpan.FromMilliseconds(10) ? TimeSpan.FromMilliseconds(10) : remaining;
    }
}
