namespace Lumyte.Graphics.TwoD;

/// <summary>An immutable, painter-ordered list of two-dimensional drawing commands.</summary>
public sealed class DisplayList
{
    private readonly RecordedCommand[] commands;

    internal DisplayList(Renderer owner, RecordedCommand[] commands)
    {
        Owner = owner;
        this.commands = commands;
    }

    public int Count => commands.Length;

    internal Renderer Owner { get; }
    internal ReadOnlySpan<RecordedCommand> Commands => commands;
}
