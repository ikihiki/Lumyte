using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed record DrawingPass2D(
    string Name,
    Action<IGraphicsCommandBuffer, IGraphicsTextureView> Record,
    Action<IDrawingResourceDeclarations2D> DeclareResources,
    IReadOnlyList<ShaderResources> Reservations,
    bool UsesTarget = true,
    bool LocalResources = false);
