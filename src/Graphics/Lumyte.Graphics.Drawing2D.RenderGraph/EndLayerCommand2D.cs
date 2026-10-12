namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>A host-owned layer structure command.</summary>
internal sealed record EndLayerCommand2D(DrawingState2D State) : DrawingCommand2D(State);
