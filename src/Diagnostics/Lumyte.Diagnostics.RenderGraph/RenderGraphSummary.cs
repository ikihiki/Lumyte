namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>Detached latest graph counts and CPU timings.</summary>
/// <param name="Name">The Name value.</param>
/// <param name="Status">The Status value.</param>
/// <param name="LivePasses">The LivePasses value.</param>
/// <param name="CulledPasses">The CulledPasses value.</param>
/// <param name="Resources">The Resources value.</param>
/// <param name="Dependencies">The Dependencies value.</param>
/// <param name="Barriers">The Barriers value.</param>
/// <param name="CompileMilliseconds">The CompileMilliseconds value.</param>
/// <param name="RecordMilliseconds">The RecordMilliseconds value.</param>
public sealed record RenderGraphSummary(string Name, string Status, long LivePasses, long CulledPasses, long Resources, long Dependencies, long Barriers, double CompileMilliseconds, double RecordMilliseconds);
