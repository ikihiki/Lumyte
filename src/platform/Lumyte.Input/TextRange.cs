namespace Lumyte.Input;

public readonly record struct TextRange(int Start, int Length)
{
    public int End => Start + Length;
}
