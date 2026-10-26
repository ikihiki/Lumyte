namespace Lumyte.Graphics.RenderGraph;

/// <summary>Requests a detached diagnostic snapshot for one compilation.</summary>
public sealed record RenderGraphCaptureOptions
{
    /// <summary>Gets the positive estimated retained byte limit, checked before allocating descriptions.</summary>
    /// <remarks>Defaults to 4 MiB. The estimate includes descriptions, arrays and strings, not GPU data or the executable plan.</remarks>
    public long MaxBytes { get; init; } = 4 * 1024 * 1024;
}
