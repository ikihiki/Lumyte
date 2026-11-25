namespace Lumyte.Graphics.Drawing2D;

/// <summary>Immutable-by-convention CPU input to the sparse GPU painter. No GPU resources are owned.</summary>
/// <remarks>WorkingBytes is conservative CPU ownership including transient preparation, not the caller's
/// later GPU/upload budget. The caller must additionally charge upload, paint, normalized clip and tables.
/// The original immutable commands retain paint and captured clip identity; this module does not average
/// or intersect shape bits with clip. The GPU must evaluate correlated samples in original order.</remarks>
internal sealed record SparseStripPlan2D(DrawingCommand2D[] Commands, SparseStrip2D[] Strips, ushort[] BoundaryMasks, long WorkingBytes);
