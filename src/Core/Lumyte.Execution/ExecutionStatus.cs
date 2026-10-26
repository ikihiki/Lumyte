namespace Lumyte.Execution;

/// <summary>Runtime lifecycle state.</summary>
public enum ExecutionStatus
{
    /// <summary>Unprepared.</summary>
    Unprepared,

    /// <summary>Created.</summary>
    Created,

    /// <summary>Starting.</summary>
    Starting,

    /// <summary>Running.</summary>
    Running,

    /// <summary>Quiescing.</summary>
    Quiescing,

    /// <summary>Stopping.</summary>
    Stopping,

    /// <summary>Stopped.</summary>
    Stopped,
}
