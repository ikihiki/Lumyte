namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    private long _lastFrame;
    private bool _recordedFrame;

    private void CompleteFrames()
    {
        using UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(7);
        long completed = 0;
        long presentedCount = 0;
        try
        {
            foreach (Entry entry in _entries.Values.Where(entry => entry.Native is not null).ToArray())
            {
                if (entry.Drawing is { IsCompleted: true } finished)
                {
                    bool presented = finished.IsCompletedSuccessfully && finished.Result;
                    completed++;
                    presentedCount += presented ? 1 : 0;
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
        catch (Exception profileError)
        {
            profile?.Failed(profileError);
            throw;
        }
        finally
        {
            profile?.Counter(6, completed);
            profile?.Counter(7, presentedCount);
        }
    }

    private void AdvanceRendering()
    {
        using UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(4);
        long accepted = 0;
        long busyCount = 0;
        long stopping = 0;
        long stopFailed = 0;
        bool counted = false;
        try
        {
            long now = TimeProvider.GetTimestamp();
            if (State == UiApplicationState.Recovering || (_recordedFrame && TimeProvider.GetElapsedTime(_lastFrame, now) < _options.FrameInterval))
            {
                if (profile is not null)
                {
                    profile.Outcome = 3;
                }

                if (UiApplicationProfiling.Log.IsEnabled(System.Diagnostics.Tracing.EventLevel.Informational, UiApplicationProfiling.Keywords.Control))
                {
                    UiApplicationProfiling.Log.FrameSuppressed(_profileRun, 0, 0, 0, 0, State == UiApplicationState.Recovering ? 1 : 2, _entries.Values.LongCount(entry => entry.Native is not null));
                }

                return;
            }

            counted = true;
            bool recorded = false;
            bool busy = false;
            foreach (Entry entry in _entries.Values.Where(entry => entry.Native is not null).ToArray())
            {
                if (entry.Drawing is not null || entry.Stopping || State == UiApplicationState.StopFailed)
                {
                    if (entry.Drawing is not null)
                    {
                        busyCount++;
                    }
                    else if (entry.Stopping)
                    {
                        stopping++;
                    }
                    else
                    {
                        stopFailed++;
                    }

                    busy |= entry.Drawing is not null && !entry.Stopping;
                    continue;
                }

                accepted++;
                entry.Drawing = RecordAndDrawFrame(entry.Root);
                entry.DrawingProfileKey = entry.Root.ProfileKey;
                recorded = true;
            }

            bool driverAdvanced = Driver.AdvanceFrames();
            recorded |= driverAdvanced;

            profile?.Counter(5, driverAdvanced ? 1 : 0);
            if (UiApplicationProfiling.Log.IsEnabled(System.Diagnostics.Tracing.EventLevel.Informational, UiApplicationProfiling.Keywords.Control))
            {
                if (busyCount != 0)
                {
                    UiApplicationProfiling.Log.FrameSuppressed(_profileRun, 0, 0, 0, 0, 3, busyCount);
                }

                if (stopping != 0)
                {
                    UiApplicationProfiling.Log.FrameSuppressed(_profileRun, 0, 0, 0, 0, 4, stopping);
                }

                if (stopFailed != 0)
                {
                    UiApplicationProfiling.Log.FrameSuppressed(_profileRun, 0, 0, 0, 0, 5, stopFailed);
                }
            }

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
        catch (Exception profileError)
        {
            profile?.Failed(profileError);
            throw;
        }
        finally
        {
            if (counted)
            {
                profile?.Counter(1, accepted);
                profile?.Counter(2, busyCount);
                profile?.Counter(3, stopping);
                profile?.Counter(4, stopFailed);
            }
        }
    }

    private TimeSpan FrameWait()
    {
        TimeSpan remaining = _recordedFrame
            ? _options.FrameInterval - TimeProvider.GetElapsedTime(_lastFrame) : _options.FrameInterval;
        return remaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1)
            : remaining > TimeSpan.FromMilliseconds(10) ? TimeSpan.FromMilliseconds(10) : remaining;
    }
}
