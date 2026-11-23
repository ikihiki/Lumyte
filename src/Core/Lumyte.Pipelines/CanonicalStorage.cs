using System.Runtime.InteropServices;

namespace Lumyte.Pipelines;

internal sealed class CanonicalStorage<TElement>(byte[] bytes) : ICanonicalStorage
    where TElement : unmanaged
{
    public TResult Accept<TResult>(ICanonicalDataVisitor<TResult> visitor)
        => visitor.Visit(MemoryMarshal.Cast<byte, TElement>(bytes));
}
