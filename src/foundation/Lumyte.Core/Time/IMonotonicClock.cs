namespace Lumyte.Core.Time;

/// <summary>Provides time that never moves backwards and is unrelated to calendar time.</summary>
public interface IMonotonicClock
{
    TimePoint Now { get; }
}
