namespace Lumyte.Graphics.TwoD;

/// <summary>A lexical drawing-state boundary owned by a <see cref="CommandEncoder"/>.</summary>
public readonly struct CommandEncoderScope : IDisposable
{
    private readonly CommandEncoder? owner;
    private readonly int id;

    internal CommandEncoderScope(CommandEncoder owner, int id)
    {
        this.owner = owner;
        this.id = id;
    }

    /// <summary>Ends this scope. Repeated disposal has no effect.</summary>
    public void Dispose() => owner?.EndScope(id);
}
