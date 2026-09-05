namespace Lumyte.Resources;

/// <summary>Provides a non-allocating view of an address inside an asset key.</summary>
public readonly struct AssetAddress
{
    private readonly string text;
    private readonly int start;
    private readonly int length;

    internal AssetAddress(string text, int start, int length)
    {
        this.text = text;
        this.start = start;
        this.length = length;
    }

    public ReadOnlyMemory<char> Text => text.AsMemory(start, length);

    public override string ToString() => new(Text.Span);
}
