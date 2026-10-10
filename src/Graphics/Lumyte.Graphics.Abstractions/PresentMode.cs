namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies the requested presentation scheduling policy.</summary>
public enum PresentMode
{
    /// <summary>Queues images for display in order at vertical synchronization.</summary>
    Fifo,

    /// <summary>Keeps the newest queued image for the next vertical synchronization.</summary>
    Mailbox,

    /// <summary>Allows presentation without waiting for vertical synchronization.</summary>
    Immediate,
}
