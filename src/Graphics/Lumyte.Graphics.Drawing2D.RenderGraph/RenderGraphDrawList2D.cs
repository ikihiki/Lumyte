namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>A reusable CPU recording of drawing, layers and effect callbacks.</summary>
public sealed class RenderGraphDrawList2D
{
    internal RenderGraphDrawList2D(DrawList2D commands)
    {
        Commands = commands;
    }

    internal DrawList2D Commands { get; }
}
