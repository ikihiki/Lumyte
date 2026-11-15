namespace Lumyte.Graphics.Drawing2D;
internal static class ClipWorkCounters2D
{
    internal static long Creates, Edges, Predicates, MaskPixels, IdentityHits, StructuralHits, HashedSegments, ComparedSegments, MaskCompareCells, MaskCopyCells;
    internal static void Reset() => Creates = Edges = Predicates = MaskPixels = IdentityHits = StructuralHits = HashedSegments = ComparedSegments = MaskCompareCells = MaskCopyCells = 0;
}
