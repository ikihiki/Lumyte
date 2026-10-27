namespace Lumyte.Execution;

/// <summary>Result of an atomic command, workflow, batch or graph admission attempt.</summary>
public enum CommandEnqueueStatus
{
    /// <summary>Accepted.</summary>
    Accepted,

    /// <summary>Canceled.</summary>
    Canceled,

    /// <summary>NotRunning.</summary>
    NotRunning,

    /// <summary>QueueFull.</summary>
    QueueFull,
}
