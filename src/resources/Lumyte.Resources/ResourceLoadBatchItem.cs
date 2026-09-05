namespace Lumyte.Resources;

public sealed class ResourceLoadBatchItem<T>
    where T : notnull
{
    internal ResourceLoadBatchItem(
        ResourceLoadBatch owner,
        AssetKey<T> key,
        int index)
    {
        Owner = owner;
        Key = key;
        Index = index;
    }

    internal ResourceLoadBatch Owner { get; }

    internal int Index { get; }

    internal ResourceHandle<T> Handle { get; set; }

    internal bool Succeeded { get; set; }

    public AssetKey<T> Key { get; }
}
