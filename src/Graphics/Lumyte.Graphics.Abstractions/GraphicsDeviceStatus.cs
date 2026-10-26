namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes the physical device lifetime.</summary>
public enum GraphicsDeviceStatus
{
    /// <summary>The device accepts work.</summary>
    Ready,

    /// <summary>The device has irreversibly lost its ability to execute work.</summary>
    Lost,

    /// <summary>The device has been disposed.</summary>
    Disposed,
}
