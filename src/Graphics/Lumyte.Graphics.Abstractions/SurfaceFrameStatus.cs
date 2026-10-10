namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies the lifetime of one acquired presentation image.</summary>
public enum SurfaceFrameStatus
{
    /// <summary>The image is acquired; the caller manages GPU submission and synchronization.</summary>
    Acquired,

    /// <summary>Presentation was requested; the image cannot be reused.</summary>
    Presented,

    /// <summary>The frame and its image lease were released.</summary>
    Disposed,
}
