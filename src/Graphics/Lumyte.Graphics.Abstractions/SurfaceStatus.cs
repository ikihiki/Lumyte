namespace Lumyte.Graphics.Abstractions;

/// <summary>Reports image acquisition or presentation without automatic recovery.</summary>
public enum SurfaceStatus
{
    /// <summary>The operation succeeded.</summary>
    Success,

    /// <summary>The image is usable, but reconfiguration is recommended.</summary>
    Suboptimal,

    /// <summary>No image was immediately available; retry later.</summary>
    Timeout,

    /// <summary>The swapchain must be explicitly reconfigured.</summary>
    Outdated,

    /// <summary>The target was lost; recreate its surface.</summary>
    Lost,

    /// <summary>The graphics device was lost; recreate the device.</summary>
    DeviceLost,
}
