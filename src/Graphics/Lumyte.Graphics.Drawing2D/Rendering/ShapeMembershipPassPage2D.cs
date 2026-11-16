using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed record ShapeMembershipPassPage2D(
    Action<IGraphicsCommandBuffer> Copy,
    Action<IDrawingResourceDeclarations2D> DeclareCopy,
    Action<IGraphicsCommandBuffer> Compute,
    Action<IDrawingResourceDeclarations2D> DeclareCompute,
    IPreparedCommandBatch2D Raster,
    IReadOnlyList<ShaderResources> RasterReservations,
    bool HasCompute);
