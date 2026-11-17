namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Explicit passive construction policy.</summary>
public static partial class DrawingCompose
{
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
