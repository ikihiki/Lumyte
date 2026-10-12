namespace Lumyte.Graphics.Hosting;

/// <summary>Describes initialization and recovery of a named device service.</summary>
public enum GraphicsDeviceServiceStatus
{
    /// <summary>Specifies Uninitialized.</summary>
    Uninitialized,

    /// <summary>Specifies Initializing.</summary>
    Initializing,

    /// <summary>Specifies Ready.</summary>
    Ready,

    /// <summary>Specifies Lost.</summary>
    Lost,

    /// <summary>Specifies Recovering.</summary>
    Recovering,

    /// <summary>Specifies Failed.</summary>
    Failed,

    /// <summary>Specifies Disposed.</summary>
    Disposed,
}
