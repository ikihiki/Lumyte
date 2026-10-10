namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies the lifetime of one acquired presentation image.</summary>
public enum SurfaceFrameStatus
{
    /// <summary>The texture may be recorded and submitted once.</summary>
    Acquired,

    /// <summary>The explicit queue submission owns the image's GPU use.</summary>
    Submitted,

    /// <summary>Presentation was requested; the image cannot be reused.</summary>
    Presented,

    /// <summary>The frame and its image lease were released.</summary>
    Disposed,
}
