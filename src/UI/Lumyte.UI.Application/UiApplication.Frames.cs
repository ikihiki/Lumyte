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

        _lastFrame = now;
        _recordedFrame = true;
        foreach (Entry entry in _entries.Values.Where(entry => entry.Native is not null).ToArray())
        {
            if (entry.Drawing is not null || entry.Stopping || State == UiApplicationState.StopFailed)
            {
                continue;
            }

            var canvas = new Canvas2D();
            canvas.Begin();
            entry.Root.Source.Record(canvas);
            DrawList2D commands = canvas.End();
            entry.Drawing = DrawFrameAsync(entry.Root, commands);
        }

        Driver.AdvanceFrames();
        foreach (TaskCompletionSource waiter in _frameWaiters)
        {
            waiter.TrySetResult();
        }

        _frameWaiters.Clear();
    }
}
