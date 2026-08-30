namespace Lumyte.Resources;

/// <summary>Provides the opened data and selector for one resource load.</summary>
public sealed class ResourceLoadContext
{
    internal ResourceLoadContext(
        AssetData data,
        string keyText,
        int selectorStart)
    {
        Data = data;
        Selector = keyText.AsMemory(selectorStart);
    }

    public AssetData Data { get; }

    public Stream Content => Data.Content;

    public ReadOnlyMemory<char> Selector { get; }
}
