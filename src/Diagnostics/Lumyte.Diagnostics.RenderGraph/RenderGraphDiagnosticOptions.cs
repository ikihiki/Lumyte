namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>Bounds render graph instrumentation and requested captures.</summary>
public sealed class RenderGraphDiagnosticOptions
{
    /// <summary>Gets or sets a value indicating whether this adapter is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the minimum interval between aggregated metric publications.</summary>
    public TimeSpan MetricInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the maximum number of pending and retained captures.</summary>
    public int MaxCaptures { get; set; } = 2;

    /// <summary>Gets or sets the estimated byte budget for one detailed snapshot.</summary>
    public long MaxSnapshotBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Gets or sets the estimated byte budget including capture records.</summary>
    public long MaxRetainedBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>Gets or sets the lifetime from the capture request, including pending time.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromSeconds(60);

    internal void Validate()
    {
        if (MetricInterval <= TimeSpan.Zero || MaxCaptures < 1 || MaxSnapshotBytes < 1
            || MaxRetainedBytes <= RenderGraphDiagnostics.CaptureOverheadBytes || Lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException("Invalid render graph diagnostic limits.");
        }
    }
}
