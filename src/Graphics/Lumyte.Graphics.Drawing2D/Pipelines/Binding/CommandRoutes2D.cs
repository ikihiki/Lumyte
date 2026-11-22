namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Captures explicit acceptance of every logical command kind for one renderer instance.</summary>
/// <param name="fillRect">Whether fillRect commands are accepted.</param>
/// <param name="fillRoundedRect">Whether fillRoundedRect commands are accepted.</param>
/// <param name="fillCircle">Whether fillCircle commands are accepted.</param>
/// <param name="drawLine">Whether drawLine commands are accepted.</param>
/// <param name="fillPath">Whether fillPath commands are accepted.</param>
/// <param name="strokePath">Whether strokePath commands are accepted.</param>
/// <param name="drawImage">Whether drawImage commands are accepted.</param>
/// <param name="drawCoverageMask">Whether drawCoverageMask commands are accepted.</param>
/// <param name="drawDistanceField">Whether drawDistanceField commands are accepted.</param>
/// <param name="drawMesh">Whether drawMesh commands are accepted.</param>
/// <param name="drawPaintGraph">Whether drawPaintGraph commands are accepted.</param>
public sealed class CommandRoutes2D(bool fillRect, bool fillRoundedRect, bool fillCircle, bool drawLine, bool fillPath, bool strokePath, bool drawImage, bool drawCoverageMask, bool drawDistanceField, bool drawMesh, bool drawPaintGraph)
{
    private readonly bool[] _accepted = [fillRect, fillRoundedRect, fillCircle, drawLine, fillPath, strokePath, drawImage, drawCoverageMask, drawDistanceField, drawMesh, drawPaintGraph];

    internal void Validate(IReadOnlyList<DrawingCommand2D> commands)
    {
        foreach (DrawingCommand2D command in commands)
        {
            int kind = command switch
            {
                FillRectCommand2D => 0,
                FillRoundedRectCommand2D => 1,
                FillCircleCommand2D => 2,
                DrawLineCommand2D => 3,
                FillPathCommand2D => 4,
                StrokePathCommand2D => 5,
                DrawImageCommand2D => 6,
                DrawCoverageMaskCommand2D => 7,
                DrawDistanceFieldCommand2D => 8,
                DrawMeshCommand2D => 9,
                DrawPaintGraphCommand2D => 10,
                _ => throw new NotSupportedException("Structural commands require the layered graph adapter."),
            };
            if (!_accepted[kind])
            {
                throw new NotSupportedException("This logical command is explicitly rejected by the renderer's immutable routes.");
            }
        }
    }
}
