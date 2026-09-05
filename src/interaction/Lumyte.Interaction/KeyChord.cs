namespace Lumyte.Interaction;

public sealed class KeyChord
{
    public KeyChord(params ReadOnlySpan<KeyStroke> strokes)
    {
        if (strokes.Length == 0)
        {
            throw new ArgumentException("A key chord must contain at least one stroke.", nameof(strokes));
        }

        Strokes = strokes.ToArray();
    }

    public IReadOnlyList<KeyStroke> Strokes { get; }
}
