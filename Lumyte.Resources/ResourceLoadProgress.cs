namespace Lumyte.Resources;

public readonly record struct ResourceLoadProgress(
    int CompletedCount,
    int TotalCount,
    int SucceededCount,
    int FailedCount)
{
    public double Fraction => TotalCount == 0
        ? 1
        : (double)CompletedCount / TotalCount;
}
