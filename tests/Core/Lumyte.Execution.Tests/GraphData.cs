using System.Collections.Concurrent;

namespace Lumyte.Execution.Tests;

internal sealed class GraphData(int length)
{
    internal int[] Values { get; } = new int[length];

    internal ConcurrentQueue<string> Events { get; } = new();

    internal int Total { get; set; }

    internal int ApplyThread { get; set; }

    internal bool FailPrepare { get; init; }

    internal bool SlowRange { get; init; }
}
