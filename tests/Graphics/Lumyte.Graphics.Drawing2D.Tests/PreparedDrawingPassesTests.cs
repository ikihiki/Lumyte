using Lumyte.Graphics.Drawing2D.RenderGraph;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks failed graph cleanup remains retryable without repeating successful releases.</summary>
public sealed class PreparedDrawingPassesTests
{
    /// <summary>Checks successful entries are removed and failed entries remain owned for the next call.</summary>
    [Fact]
    public void CleanupFailureRetainsOnlyUnreleasedResources()
    {
        var calls = new List<string>();
        var first = new Resource("first", calls);
        var retry = new Resource("retry", calls, failOnce: true);
        var last = new Resource("last", calls);
        using var prepared = new PreparedDrawingPasses2D();
        prepared.Own(first);
        prepared.Own(retry);
        prepared.Own(last);
        Assert.Throws<AggregateException>(prepared.Dispose);
        Assert.Equal(["last", "retry", "first"], calls);
        Assert.Throws<ObjectDisposedException>(prepared.BeginRecord);
        Assert.Throws<ObjectDisposedException>(() => prepared.Own(first));
        prepared.Dispose();
        prepared.Dispose();
        Assert.Equal(["last", "retry", "first", "retry"], calls);
    }

    private sealed class Resource(string name, List<string> calls, bool failOnce = false) : IDisposable
    {
        private bool _failed;

        public void Dispose()
        {
            calls.Add(name);
            if (failOnce && !_failed)
            {
                _failed = true;
                throw new InvalidOperationException("Retry cleanup.");
            }
        }
    }
}
