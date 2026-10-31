namespace Lumyte.Signals;

/// <summary>Optionally delivers coalesced observer work to a host's update boundary.</summary>
public interface ISignalScheduler
{
    /// <summary>Schedules one synchronous work item after the originating evaluation.</summary>
    /// <param name="work">A revocable observer operation.</param>
    void Schedule(Action work);
}
