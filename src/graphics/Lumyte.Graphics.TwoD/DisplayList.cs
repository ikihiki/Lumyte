namespace Lumyte.Graphics.TwoD;

/// <summary>An immutable, painter-ordered list of two-dimensional drawing commands.</summary>
public sealed class DisplayList
{
    private readonly RecordedCommand[] commands;
    private readonly RecordedLayer[] layers;

    internal DisplayList(Renderer owner, RecordedCommand[] commands, RecordedLayer[] layers)
    {
        Owner = owner;
        this.commands = commands;
        this.layers = layers;
    }

    public int Count => commands.Length;

    internal Renderer Owner { get; }
    internal ReadOnlySpan<RecordedCommand> Commands => commands;
    internal ReadOnlySpan<RecordedLayer> Layers => layers;
}
