using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks failure owners remain reachable before any prepared batch is published.</summary>
public sealed class DrawingResourceCleanupTests
{
    /// <summary>A root-only owner failure is retried, including a disposer that validates its context.</summary>
    [Fact]
    public void RootOnlyCleanupFailureRetainsReentrantRetryOwner()
    {
        using var resources = new DrawingResourceState2D(new ProfilingDevice());
        var disposable = new ThrowOnce(resources.Validate);
        using IDisposable token = resources.CleanupOwner(disposable);
        Assert.Throws<InvalidOperationException>(token.Dispose);
        Assert.Equal(1, disposable.Attempts);
        resources.Dispose();
        resources.Dispose();
        token.Dispose();
        Assert.Equal(2, disposable.Attempts);
    }

    /// <summary>An unpublished batch keeps both failure causes and a retry owner.</summary>
    [Fact]
    public void PartialPreparationFailurePreservesBothExceptionsAndOwnership()
    {
        using var resources = new DrawingResourceState2D(new ProfilingDevice());
        var disposable = new ThrowOnce(resources.Validate);
        var original = new ArgumentException("Preparation failed.");
        AggregateException failure = Assert.Throws<AggregateException>(() => resources.CleanupAfterFailure(disposable, original));
        Assert.Same(original, failure.InnerExceptions[0]);
        Assert.IsType<InvalidOperationException>(failure.InnerExceptions[1]);
        resources.Dispose();
        Assert.Equal(2, disposable.Attempts);
    }

    private sealed class ThrowOnce(Action onRetry) : IDisposable
    {
        internal int Attempts { get; private set; }

        public void Dispose()
        {
            Attempts++;
            if (Attempts == 1)
            {
                throw new InvalidOperationException("Injected root disposal failure.");
            }

            onRetry();
        }
    }
}
