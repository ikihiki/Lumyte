using System.Diagnostics;

namespace Lumyte.Graphics.Drawing2D;

internal readonly record struct DrawingProfileContext(Guid RunId, long FrameId)
{
    internal static DrawingProfileContext Capture()
    {
        for (Activity? activity = Activity.Current; activity is not null; activity = activity.Parent)
        {
            if (activity.GetCustomProperty("Lumyte.Profiling.RunId") is Guid runId && activity.GetCustomProperty("Lumyte.Profiling.FrameId") is long frameId)
            {
                return new(runId, frameId);
            }
        }

        return default;
    }
}
