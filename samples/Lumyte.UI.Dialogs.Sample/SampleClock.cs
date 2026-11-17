using Lumyte.Core.Time;

namespace Lumyte.UI.Dialogs.Sample;

/// <summary>An explicitly borrowed monotonic clock, advanced only through the application owner dispatcher.</summary>
internal sealed class SampleClock : IMonotonicClock
{
    public TimePoint Now { get; set; }
}
