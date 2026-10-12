namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>Describes one bounded capture without retaining native resources.</summary>
/// <param name="CaptureId">The stable capture identifier.</param>
/// <param name="GraphId">The graph identifier, or empty while pending.</param>
/// <param name="RunId">The compilation identifier, or empty while pending.</param>
/// <param name="Name">The graph name or pending name filter.</param>
/// <param name="FrameId">The caller's frame identifier, or -1 while pending.</param>
/// <param name="State">Pending, capturing, compiled, recorded, compile-failed, record-failed, or too-large.</param>
/// <param name="LivePasses">The number of live passes.</param>
/// <param name="CulledPasses">The number of culled passes.</param>
/// <param name="Resources">The number of declared resources.</param>
/// <param name="TransientResources">The number of declared temporary resources, including culled allocations.</param>
/// <param name="Dependencies">The number of live ordering dependencies.</param>
/// <param name="Barriers">The number of planned barriers.</param>
/// <param name="CompileMilliseconds">The successful compile CPU duration.</param>
/// <param name="RecordMilliseconds">The successful recording CPU duration.</param>
/// <param name="EstimatedBytes">The estimated retained bytes, including record overhead.</param>
public sealed record RenderGraphCaptureDiagnostic(string CaptureId, string GraphId, string RunId, string Name, long FrameId, string State, long LivePasses, long CulledPasses, long Resources, long TransientResources, long Dependencies, long Barriers, double CompileMilliseconds, double RecordMilliseconds, long EstimatedBytes);
