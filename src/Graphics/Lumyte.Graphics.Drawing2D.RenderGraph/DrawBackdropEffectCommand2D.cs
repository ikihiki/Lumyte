namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>A host-owned layer structure command.</summary>
internal sealed record DrawBackdropEffectCommand2D(BackdropEffectOptions2D Options, DrawingState2D State) : DrawingCommand2D(State);
