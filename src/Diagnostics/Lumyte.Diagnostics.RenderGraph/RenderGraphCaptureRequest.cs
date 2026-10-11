namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>Identifies a requested graph capture.</summary>
/// <param name="CaptureId">The stable identifier used by capture inspection operations.</param>
/// <param name="State">The initial pending state.</param>
public sealed record RenderGraphCaptureRequest(string CaptureId, string State);
