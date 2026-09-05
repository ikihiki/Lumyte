namespace Lumyte.Resources;

public sealed class ResourceLoadBatchResult
{
    internal ResourceLoadBatchResult(
        ResourceLoadBatch owner,
        int totalCount,
        int succeededCount,
        IReadOnlyList<ResourceLoadBatchFailure> failures)
    {
        Owner = owner;
        TotalCount = totalCount;
        SucceededCount = succeededCount;
        Failures = failures;
    }

    internal ResourceLoadBatch Owner { get; }

    public int TotalCount { get; }

    public int SucceededCount { get; }

    public int FailedCount => Failures.Count;

    public bool IsSuccess => FailedCount == 0;

    public IReadOnlyList<ResourceLoadBatchFailure> Failures { get; }

    public ResourceHandle<T> Get<T>(ResourceLoadBatchItem<T> item)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateOwner(item.Owner);
        if (!item.Succeeded)
        {
            throw new InvalidOperationException(
                $"The batch item '{item.Key}' did not load successfully.");
        }

        return item.Handle;
    }

    public bool TryGet<T>(
        ResourceLoadBatchItem<T> item,
        out ResourceHandle<T> handle)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateOwner(item.Owner);
        handle = item.Handle;
        return item.Succeeded;
    }

    private void ValidateOwner(ResourceLoadBatch owner)
    {
        if (!ReferenceEquals(Owner, owner))
        {
            throw new ArgumentException(
                "The batch item belongs to a different resource load batch.");
        }
    }
}
