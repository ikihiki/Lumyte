namespace Lumyte.Graphics.Drawing2D;

/// <summary>The evaluation required after a strip's binary membership test.</summary>
internal enum SparseStripEvaluation2D
{
    Samples,
    Image,
    CoverageMask,
    DistanceField,
    MeshCenter,
}
