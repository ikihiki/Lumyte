namespace Lumyte.Graphics.Portable.Resources.Tests.Unit.Utilities;

public sealed class GpuResourcePoolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReacquiringReturnedResourceUsesANewLoanWithTheSameHandle(bool texture)
    {
        using var pool = new PoolFixture(texture);
        object first = pool.Acquire();
        object handle = pool.Handle(first);
        object description = pool.Description(first);

        pool.Release(first);
        object second = pool.Acquire();

        Assert.NotSame(first, second);
        Assert.Same(handle, pool.Handle(second));
        Assert.Equal(description, pool.Description(first));
        Assert.Single(pool.Backend.Created);
        pool.Release(second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimultaneousLoansNeverShareAResource(bool texture)
    {
        using var pool = new PoolFixture(texture);
        object first = pool.Acquire();

        object second = pool.Acquire();

        Assert.NotSame(pool.Handle(first), pool.Handle(second));
        pool.Release(first);
        pool.Release(second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleReleaseCannotReturnAReacquiredResource(bool texture)
    {
        using var pool = new PoolFixture(texture);
        object old = pool.Acquire();
        pool.Release(old);
        object current = pool.Acquire();
        object currentHandle = pool.Handle(current);

        ArgumentException error = Assert.Throws<ArgumentException>(() => pool.Release(old));
        object another = pool.Acquire();

        Assert.Equal("lease", error.ParamName);
        Assert.Contains("already been returned", error.Message, StringComparison.Ordinal);
        Assert.NotSame(currentHandle, pool.Handle(another));
        pool.Release(current);
        pool.Release(another);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnedLoanRejectsFurtherHandleAccess(bool texture)
    {
        using var pool = new PoolFixture(texture);
        object loan = pool.Acquire();

        pool.Release(loan);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => pool.Handle(loan));
        Assert.Contains("already been returned", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignPoolCannotAcceptAnotherPoolsLoan(bool texture)
    {
        var backend = new TestBackend();
        using var first = new PoolFixture(texture, backend);
        using var second = new PoolFixture(texture, backend);
        object loan = first.Acquire();

        ArgumentException error = Assert.Throws<ArgumentException>(() => second.Release(loan));

        Assert.Equal("lease", error.ParamName);
        Assert.Contains("another resource pool", error.Message, StringComparison.Ordinal);
        Assert.Same(Assert.Single(backend.Created), first.Handle(loan));
        first.Release(loan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrimDestroysOnlyIdleResources(bool texture)
    {
        using var pool = new PoolFixture(texture);
        object idle = pool.Acquire();
        object idleHandle = pool.Handle(idle);
        object active = pool.Acquire();
        object activeHandle = pool.Handle(active);
        pool.Release(idle);

        pool.Trim();

        Assert.Same(idleHandle, Assert.Single(pool.Backend.Destroyed));
        Assert.Same(activeHandle, pool.Handle(active));
        pool.Release(active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutstandingLoansRejectDisposalWithoutChangingThePool(bool texture)
    {
        using var pool = new PoolFixture(texture);
        object idle = pool.Acquire();
        object idleHandle = pool.Handle(idle);
        object active = pool.Acquire();
        pool.Release(idle);

        Assert.Throws<InvalidOperationException>(() => pool.Dispose());
        object reused = pool.Acquire();

        Assert.Same(idleHandle, pool.Handle(reused));
        Assert.Empty(pool.Backend.Destroyed);
        pool.Release(active);
        pool.Release(reused);
        pool.Dispose();
        Assert.Equal(2, pool.Backend.Destroyed.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposeDestroysIdleResourcesOnceAndKeepsBackendAlive(bool texture)
    {
        var pool = new PoolFixture(texture);
        object loan = pool.Acquire();
        object handle = pool.Handle(loan);
        pool.Release(loan);

        pool.Dispose();
        pool.Dispose();

        Assert.Same(handle, Assert.Single(pool.Backend.Destroyed));
        Assert.False(pool.Backend.Disposed);
        Assert.Throws<ObjectDisposedException>(() => pool.Acquire());
        Assert.Throws<ObjectDisposedException>(() => pool.Trim());
        Assert.Throws<ObjectDisposedException>(() => pool.Release(loan));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCreationDoesNotLeaveAnOutstandingLoan(bool texture)
    {
        using var pool = new PoolFixture(texture);
        var failure = new InvalidOperationException("create failed");
        pool.Backend.CreationError = failure;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => pool.Acquire());
        pool.Backend.CreationError = null;
        object loan = pool.Acquire();
        pool.Release(loan);
        pool.Dispose();

        Assert.Same(failure, error);
        Assert.Single(pool.Backend.Created);
        Assert.Single(pool.Backend.Destroyed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedTrimAttemptsEveryReleaseAndNeverReusesAmbiguousHandles(bool texture)
    {
        using var pool = new PoolFixture(texture);
        object first = pool.Acquire();
        object second = pool.Acquire();
        object firstHandle = pool.Handle(first);
        object secondHandle = pool.Handle(second);
        var failure = new InvalidOperationException("destroy failed");
        pool.Backend.DestructionErrors.Add(firstHandle, failure);
        pool.Release(first);
        pool.Release(second);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => pool.Trim());
        pool.Trim();
        object next = pool.Acquire();

        Assert.Same(failure, error);
        Assert.Equal(2, pool.Backend.Destroyed.Count);
        Assert.Contains(firstHandle, pool.Backend.Destroyed);
        Assert.Contains(secondHandle, pool.Backend.Destroyed);
        Assert.NotSame(firstHandle, pool.Handle(next));
        Assert.NotSame(secondHandle, pool.Handle(next));
        pool.Release(next);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedDisposalPreservesAllErrorsWithoutRetryingDestruction(bool texture)
    {
        var pool = new PoolFixture(texture);
        object first = pool.Acquire();
        object second = pool.Acquire();
        var firstFailure = new InvalidOperationException("first destroy failed");
        var secondFailure = new InvalidOperationException("second destroy failed");
        pool.Backend.DestructionErrors.Add(pool.Handle(first), firstFailure);
        pool.Backend.DestructionErrors.Add(pool.Handle(second), secondFailure);
        pool.Release(first);
        pool.Release(second);

        AggregateException error = Assert.Throws<AggregateException>(() => pool.Dispose());
        pool.Dispose();

        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Contains(firstFailure, error.InnerExceptions);
        Assert.Contains(secondFailure, error.InnerExceptions);
        Assert.Equal(2, pool.Backend.Destroyed.Count);
        Assert.False(pool.Backend.Disposed);
        Assert.Throws<ObjectDisposedException>(() => pool.Acquire());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupAggregatesOnlyFailedDestructionsAndStillAttemptsTheRest(bool texture)
    {
        using var pool = new PoolFixture(texture);
        object first = pool.Acquire();
        object second = pool.Acquire();
        object third = pool.Acquire();
        var firstFailure = new InvalidOperationException("first destroy failed");
        var thirdFailure = new InvalidOperationException("third destroy failed");
        pool.Backend.DestructionErrors.Add(pool.Handle(first), firstFailure);
        pool.Backend.DestructionErrors.Add(pool.Handle(third), thirdFailure);
        pool.Release(first);
        pool.Release(second);
        pool.Release(third);

        AggregateException error = Assert.Throws<AggregateException>(() => pool.Trim());

        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Contains(firstFailure, error.InnerExceptions);
        Assert.Contains(thirdFailure, error.InnerExceptions);
        Assert.Equal(3, pool.Backend.Destroyed.Count);
    }

    private sealed class PoolFixture : IDisposable
    {
        private readonly GpuBufferPool? buffers;
        private readonly GpuTexturePool? textures;
        internal TestBackend Backend { get; }

        internal PoolFixture(bool texture, TestBackend? backend = null)
        {
            Backend = backend ?? new();
            if (texture) { textures = new(Backend); }
            else { buffers = new(Backend); }
        }

        internal object Acquire() => buffers is not null ? buffers.Acquire(new(64, GpuBufferUsage.Storage))
            : textures!.Acquire(new(GpuTextureDimension.Texture2D, 4, 4, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));
        internal object Handle(object lease) => lease is GpuBufferLease buffer ? buffer.Handle : ((GpuTextureLease)lease).Handle;
        internal object Description(object lease) => lease is GpuBufferLease buffer ? buffer.Description : ((GpuTextureLease)lease).Description;
        internal void Release(object lease)
        {
            if (buffers is not null) { buffers.Release((GpuBufferLease)lease); }
            else { textures!.Release((GpuTextureLease)lease); }
        }
        internal void Trim()
        {
            if (buffers is not null) { buffers.Trim(); }
            else { textures!.Trim(); }
        }
        public void Dispose()
        {
            if (buffers is not null) { buffers.Dispose(); }
            else { textures!.Dispose(); }
        }
    }
}
