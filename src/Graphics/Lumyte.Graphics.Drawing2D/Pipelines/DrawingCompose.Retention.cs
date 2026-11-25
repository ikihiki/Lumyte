namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Explicit passive construction policy.</summary>
public static partial class DrawingCompose
{
    /// <summary>Enables immutable GPU payload sharing with explicit bounds.</summary>
    /// <param name="maxFramesInFlight">Maximum live or quarantined frames.</param>
    /// <param name="maxCachedAssets">Maximum idle chunks.</param>
    /// <param name="maxCachedBytes">Conservative retained memory budget, not global VRAM.</param>
    /// <param name="maxWorkingBytes">Maximum conservative preparation working set.</param>
    /// <returns>The passive GPU retention policy.</returns>
    public static DrawingRetentionOptions2D RetentionGpuLimits(int maxFramesInFlight, int maxCachedAssets, long maxCachedBytes, long maxWorkingBytes)
    {
        DrawingRetentionOptions2D options = RetentionLimits(maxFramesInFlight, maxCachedAssets, maxCachedBytes) with { EnableResidentInstances = true, MaxWorkingBytes = maxWorkingBytes };
        options.Validate();
        return options;
    }

    /// <summary>Creates retention bounds with every semantic limit explicitly supplied.</summary>
    /// <param name="maxFramesInFlight">Maximum active frames.</param>
    /// <param name="maxCachedAssets">Maximum idle reusable batches.</param>
    /// <param name="maxCachedBytes">Retained memory budget.</param>
    /// <returns>The validated passive configuration.</returns>
    public static DrawingRetentionOptions2D RetentionLimits(int maxFramesInFlight, int maxCachedAssets, long maxCachedBytes)
    {
        var options = new DrawingRetentionOptions2D { MaxFramesInFlight = maxFramesInFlight, MaxCachedAssets = maxCachedAssets, MaxCachedBytes = maxCachedBytes };
        options.Validate();
        return options;
    }
}
