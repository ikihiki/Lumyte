using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    private Guid _profileRun;
    private long _profileFrame;
    private long _profileMetadataTimestamp;
    private bool _profileMetadataEmitted;

    internal Task<bool> RecordAndDrawFrame(UiDriverWindow target)
    {
        target.ProfileKey = default;
        if (!UiApplicationProfiling.Log.IsEnabled())
        {
            return DrawFrameAsync(target, target.Source.RecordRetainedFrame(GraphicsGeneration));
        }

        Activity? previous = Activity.Current;
        Activity? activity = null;
        UiApplicationProfiling.Key key = default;
        if (UiApplicationProfiling.Log.IsEnabled(EventLevel.Informational, UiApplicationProfiling.Keywords.Control))
        {
            if (_profileRun == Guid.Empty)
            {
                _profileRun = Guid.NewGuid();
            }

            long now = Stopwatch.GetTimestamp();
            if (!_profileMetadataEmitted || Stopwatch.GetElapsedTime(_profileMetadataTimestamp, now) >= TimeSpan.FromSeconds(1))
            {
                _profileMetadataTimestamp = now;
                _profileMetadataEmitted = true;
                UiApplicationProfiling.Log.RunMetadata(_profileRun, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), Driver.GetType().FullName ?? "unknown", typeof(UiApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown", "unknown", "unknown", "unknown", "unknown", "unknown");
            }

            key = new(_profileRun, ++_profileFrame);
            target.ProfileKey = key;
            activity = new Activity("Lumyte.UI.Frame").Start();
            activity.SetCustomProperty("Lumyte.Profiling.RunId", key.Run);
            activity.SetCustomProperty("Lumyte.Profiling.FrameId", key.Frame);
            UiApplicationProfiling.Log.FrameAccepted(key.Run, key.Frame, 0, 0, 0, target.Source.SourceId, unchecked((long)GraphicsGeneration));
        }

        try
        {
            DrawingSnapshot2D scene;
            using (UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(5))
            {
                try
                {
                    scene = target.Source.RecordRetainedFrame(GraphicsGeneration);
                }
                catch (Exception error)
                {
                    profile?.Failed(error);
                    throw;
                }
            }

            return activity is null ? DrawFrameAsync(target, scene) : FinishProfiledFrameAsync(target, scene, key, activity);
        }
        catch (Exception error)
        {
            if (key.Frame != 0)
            {
                UiApplicationProfiling.Log.FrameFinished(key.Run, key.Frame, 0, 0, 0, error is OperationCanceledException ? 1 : 2, false, target.Source.SourceId);
                activity?.Dispose();
            }

            throw;
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    private async Task<bool> FinishProfiledFrameAsync(UiDriverWindow target, DrawingSnapshot2D scene, UiApplicationProfiling.Key key, Activity? activity)
    {
        bool presented = false;
        int outcome = 2;
        try
        {
            presented = await DrawFrameAsync(target, scene);
            outcome = presented ? 0 : 3;
            return presented;
        }
        catch (OperationCanceledException)
        {
            outcome = 1;
            throw;
        }
        finally
        {
            if (key.Frame != 0)
            {
                UiApplicationProfiling.Log.FrameFinished(key.Run, key.Frame, 0, 0, 0, outcome, presented, target.Source.SourceId);
                activity?.Dispose();
            }
        }
    }
}
