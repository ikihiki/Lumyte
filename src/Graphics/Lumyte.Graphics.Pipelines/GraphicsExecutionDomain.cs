namespace Lumyte.Graphics.Pipelines;

/// <summary>Declares a graphics operation domain, not an executable capability.</summary>
public enum GraphicsExecutionDomain
{
    /// <summary>Runs a compute program.</summary>
    Compute,

    /// <summary>Runs a vertex/fragment program.</summary>
    Raster,

    /// <summary>Runs a mesh program.</summary>
    Mesh,

    /// <summary>Explicitly uploads host data.</summary>
    Upload,

    /// <summary>Explicitly downloads device data.</summary>
    Download,
}
