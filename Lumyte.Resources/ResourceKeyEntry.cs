namespace Lumyte.Resources;

internal sealed class ResourceKeyEntry
{
    internal ResourceKeyEntry(
        uint slot,
        string text,
        RuntimeTypeHandle resultType,
        int addressStart,
        int selectorStart)
    {
        int addressEnd = selectorStart == text.Length
            ? selectorStart
            : selectorStart - 1;

        Slot = slot;
        Text = text;
        ResultType = resultType;
        Scheme = text[..(addressStart - 1)];
        Address = new AssetAddress(
            text,
            addressStart,
            addressEnd - addressStart);
        SelectorStart = selectorStart;
        SelectorLength = text.Length - selectorStart;
    }

    internal uint Slot { get; }

    internal string Text { get; }

    internal RuntimeTypeHandle ResultType { get; }

    internal string Scheme { get; }

    internal AssetAddress Address { get; }

    internal int SelectorStart { get; }

    internal int SelectorLength { get; }
}
