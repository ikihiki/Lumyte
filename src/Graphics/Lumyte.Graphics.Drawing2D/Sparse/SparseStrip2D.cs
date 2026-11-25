namespace Lumyte.Graphics.Drawing2D;

/// <summary>A disjoint horizontal run in target-local integer pixels, in painter order.</summary>
/// <remarks>MaskOffset is -1 for full membership. Otherwise Length ushort values are stored;
/// bit (4*y+x) identifies sample (x+.5,y+.5)/4. Full runs never allocate masks.
/// CommandOrdinal indexes Commands; its paint, clip and resource descriptors remain borrowed.
/// Image/field runs are conservative rectangles, not CPU texture coverage. MeshCenter requires
/// the original mesh's center interpolation before multiplying per-sample paint and clip.</remarks>
internal readonly record struct SparseStrip2D(int CommandOrdinal, int X, int Y, int Length, int MaskOffset, SparseStripEvaluation2D Evaluation);
