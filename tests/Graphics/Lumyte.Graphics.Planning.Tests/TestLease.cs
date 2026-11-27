namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal sealed class TestLease : IDisposable
{
    internal int Disposals { get; private set; }

    public void Dispose() => Disposals++;
}
