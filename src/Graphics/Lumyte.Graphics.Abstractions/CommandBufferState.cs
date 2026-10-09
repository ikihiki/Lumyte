namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies CommandBufferState values.</summary>
public enum CommandBufferState
{
    /// <summary>Specifies Recording.</summary>
    Recording,

    /// <summary>Specifies Executable.</summary>
    Executable,

    /// <summary>Specifies Submitted.</summary>
    Submitted,

    /// <summary>Specifies Completed.</summary>
    Completed,

    /// <summary>Specifies Faulted.</summary>
    Faulted,

    /// <summary>Specifies Disposed.</summary>
    Disposed,
}
