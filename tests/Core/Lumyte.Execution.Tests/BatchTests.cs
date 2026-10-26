using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Checks atomic typed staging, retained results, failure, and generation validation.</summary>
public sealed class BatchTests
{
    /// <summary>A full batch is accepted in one FIFO interval and results remain typed.</summary>
    [Fact]
    public void MixedTypesWithinOneGroupAreAtomicAndTyped()
    {
        using var fixture = new ExecutionFixture();
        using var batch = new CommandBatch(3);
        fixture.Start();
        int first = 1;
        long second = 2;
        int third = 3;
        CommandResultKey<int> a = batch.Add(fixture.Module.Port, in first);
        CommandResultKey<long> b = batch.Add(fixture.Module.OtherPort, in second);
        CommandResultKey<int> c = batch.Add(fixture.Module.Port, in third);
        Assert.True(fixture.Runtime.TrySubmit(batch, out CommandRun? run));
        Assert.Throws<InvalidOperationException>(() => batch.Reset());
        fixture.Until(() => run!.IsCompleted);
        run!.ThrowIfFailed();
        Assert.Equal(2, run.GetResult(a));
        Assert.Equal(6, run.GetResult(b));
        Assert.Equal(6, run.GetResult(c));
        Assert.Equal(new[] { 1, 2, 3 }, fixture.Module.Executed.ToArray());
        run.Dispose();
        batch.Reset();
        Assert.Throws<ObjectDisposedException>(() => run.GetResult(a));
    }

    /// <summary>Capacity rejection sends no partial work and preserves staging for retry.</summary>
    [Fact]
    public void CapacityRejectionPreservesWholeBatch()
    {
        using var fixture = new ExecutionFixture(options: new ExecutionOptions { MaxCommands = 2 });
        fixture.Start();
        ValueTask<int> retained = fixture.Module.Port.ExecuteAsync(5);
        fixture.Until(() => retained.IsCompleted);
        using var batch = new CommandBatch(2);
        batch.Add(fixture.Module.Port, 1);
        batch.Add(fixture.Module.Port, 2);
        Assert.False(fixture.Runtime.TrySubmit(batch, out CommandRun? rejected));
        Assert.Null(rejected);
        Assert.Equal(new[] { 5 }, fixture.Module.Executed.ToArray());
        CompletedResult.Read(retained);
        Assert.True(fixture.Runtime.TrySubmit(batch, out CommandRun? accepted));
        fixture.Until(() => accepted!.IsCompleted);
        accepted!.ThrowIfFailed();
        accepted.Dispose();
    }

    /// <summary>A handler failure preserves earlier successes and suppresses later handlers.</summary>
    [Fact]
    public void FailureStopsUnstartedFollowersButKeepsSuccessfulResults()
    {
        using var fixture = new ExecutionFixture();
        fixture.Start();
        using var batch = new CommandBatch(3);
        CommandResultKey<int> first = batch.Add(fixture.Module.Port, 1);
        batch.Add(fixture.Module.Port, 999);
        batch.Add(fixture.Module.Port, 3);
        Assert.True(fixture.Runtime.TrySubmit(batch, out CommandRun? run));
        fixture.Until(() => run!.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => run!.ThrowIfFailed());
        Assert.Equal(2, run!.GetResult(first));
        Assert.Equal(new[] { 1 }, fixture.Module.Executed.ToArray());
        run.Dispose();
    }

    /// <summary>Explicit batch cancellation suppresses gate-blocked work and waits for quiet completion.</summary>
    [Fact]
    public void RequestCancelCompletesWaitingBatch()
    {
        using var fixture = new ExecutionFixture(gated: true);
        fixture.Start();
        using var batch = new CommandBatch(2);
        batch.Add(fixture.Module.Port, 1);
        batch.Add(fixture.Module.Port, 2);
        Assert.True(fixture.Runtime.TrySubmit(batch, out CommandRun? run));
        run!.RequestCancel();
        fixture.Until(() => run.IsCompleted);
        Assert.Throws<OperationCanceledException>(() => run.ThrowIfFailed());
        Assert.Empty(fixture.Module.Executed);
        run.Dispose();
    }

    /// <summary>Results from another batch generation cannot be read through a new run.</summary>
    [Fact]
    public void ResetInvalidatesPreviousResultKeys()
    {
        using var fixture = new ExecutionFixture();
        fixture.Start();
        using var batch = new CommandBatch(1);
        CommandResultKey<int> old = batch.Add(fixture.Module.Port, 1);
        Assert.True(fixture.Runtime.TrySubmit(batch, out CommandRun? first));
        fixture.Until(() => first!.IsCompleted);
        first!.Dispose();
        batch.Reset();
        CommandResultKey<int> current = batch.Add(fixture.Module.Port, 2);
        Assert.True(fixture.Runtime.TrySubmit(batch, out CommandRun? second));
        fixture.Until(() => second!.IsCompleted);
        Assert.Throws<ArgumentException>(() => second!.GetResult(old));
        Assert.Equal(4, second!.GetResult(current));
        second.Dispose();
    }

    /// <summary>Domain cancellation exceptions fault the batch completion task.</summary>
    [Fact]
    public void HandlerCancellationExceptionFaultsBatch()
    {
        using var fixture = new ExecutionFixture();
        fixture.Start();
        using var batch = new CommandBatch(1);
        batch.Add(fixture.Module.Port, -999);
        Assert.True(fixture.Runtime.TrySubmit(batch, out CommandRun? run));
        fixture.Until(() => run!.IsCompleted);
        Assert.True(run!.Completion.IsFaulted);
        Assert.Throws<OperationCanceledException>(() => run.ThrowIfFailed());
        run.Dispose();
    }
}
