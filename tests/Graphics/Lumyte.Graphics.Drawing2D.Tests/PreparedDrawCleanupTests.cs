using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks failed batch returns can be retried without returning successful pools twice.</summary>
public sealed class PreparedDrawCleanupTests
{
    /// <summary>A failed cleanup keeps only its own remaining resources.</summary>
    [Fact]
    public void RetriesOnlyFailedBatchReturns()
    {
        var retry = new Batch(true);
        var success = new Batch(false);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using var prepared = new PreparedDraw2D(new() { View = view }, [retry, success]);
        Assert.Throws<AggregateException>(prepared.Dispose);
        Assert.Equal(1, success.Disposals);
        Assert.Throws<ObjectDisposedException>(() => prepared.Record(new DispatchCommands()));
        prepared.Dispose();
        prepared.Dispose();
        Assert.Equal(1, success.Disposals);
        Assert.Equal(2, retry.Disposals);
    }

    private sealed class Batch(bool fail) : IPreparedCommandBatch2D
    {
        internal int Disposals { get; private set; }

        public void DeclareResources(IDrawingResourceDeclarations2D resources) => throw new NotSupportedException();

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target) => throw new NotSupportedException();

        public void Dispose()
        {
            Disposals++;
            if (fail && Disposals == 1)
            {
                throw new InvalidOperationException("Retry cleanup.");
            }
        }
    }
}
