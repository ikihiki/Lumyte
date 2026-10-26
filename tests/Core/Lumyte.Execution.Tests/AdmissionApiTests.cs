using System.Threading.Tasks.Sources;
using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Checks common admission outcomes while preserving legacy callers.</summary>
public sealed class AdmissionApiTests
{
    /// <summary>Detailed command admission keeps the actual result and reports all rejection reasons.</summary>
    [Fact]
    public void CommandAdmissionUsesOptionalCancellationAndRetainsResults()
    {
        using var fixture = new ExecutionFixture(options: new ExecutionOptions { MaxInFlightRuns = 1 });
        int command = 7;
        Assert.Equal(CommandEnqueueStatus.NotRunning, fixture.Module.Port.Enqueue(in command, out IValueTaskSource<int>? source));
        Assert.Null(source);
        fixture.Start();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Equal(CommandEnqueueStatus.Canceled, fixture.Module.Port.Enqueue(in command, out source, cancellationToken: canceled.Token));
        Assert.Null(source);
        Assert.Equal(CommandEnqueueStatus.Accepted, fixture.Module.Port.Enqueue(in command, out source));
        var result = new ValueTask<int>(source!, 0);
        fixture.Until(() => result.IsCompleted);
        Assert.Equal(CommandEnqueueStatus.QueueFull, fixture.Module.Port.Enqueue(in command, out source));
        Assert.Null(source);
        Assert.Equal(14, CompletedResult.Read(result));
        Assert.Equal(14, fixture.Wait(fixture.Module.Port.ExecuteAsync(command, token: default)));
    }

    /// <summary>Rejected batches retain their staged commands; legacy cancellation and lifecycle errors remain exceptions.</summary>
    [Fact]
    public void BatchAdmissionReportsStatusesAndPreservesLegacyExceptions()
    {
        using var fixture = new ExecutionFixture(options: new ExecutionOptions { MaxInFlightRuns = 1 });
        using var batch = new CommandBatch(1);
        batch.Add(fixture.Module.Port, 3);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Equal(CommandEnqueueStatus.NotRunning, fixture.Runtime.Submit(batch, out CommandRun? run, canceled.Token));
        Assert.Null(run);
        Assert.Throws<InvalidOperationException>(() => fixture.Runtime.TrySubmit(batch, out _));
        Assert.Throws<OperationCanceledException>(() => fixture.Runtime.TrySubmit(batch, out _, token: canceled.Token));
        fixture.Start();
        Assert.Equal(CommandEnqueueStatus.Canceled, fixture.Runtime.Submit(batch, out run, cancellationToken: canceled.Token));
        Assert.Null(run);
        Assert.Throws<OperationCanceledException>(() => fixture.Runtime.TrySubmit(batch, out _, token: canceled.Token));
        ValueTask<int> retained = fixture.Module.Port.ExecuteAsync(2);
        fixture.Until(() => retained.IsCompleted);
        Assert.Equal(CommandEnqueueStatus.QueueFull, fixture.Runtime.Submit(batch, out run));
        Assert.Null(run);
        CompletedResult.Read(retained);
        Assert.Equal(CommandEnqueueStatus.Accepted, fixture.Runtime.Submit(batch, out run));
        fixture.Until(() => run!.IsCompleted);
        run!.ThrowIfFailed();
        run.Dispose();
        batch.Reset();
        batch.Add(fixture.Module.Port, 4);
        if (fixture.Runtime.TrySubmit(batch, out CommandRun? legacy))
        {
            // Compiles without suppression because success promises a non-null run.
            fixture.Until(() => legacy.IsCompleted);
            legacy.ThrowIfFailed();
            legacy.Dispose();
        }
        else
        {
            Assert.Fail("Disposing the prior run should return capacity.");
        }

        Assert.Equal(new[] { 2, 3, 4 }, fixture.Module.Executed.ToArray());
    }

    /// <summary>Graph admission exposes the same statuses and leaves all capacity available after rejection.</summary>
    [Fact]
    public void GraphAdmissionReportsStatusesAndPreservesLegacyExceptions()
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions { MaxInFlightRuns = 1 });
        var builder = new JobGraphBuilder<GraphInput>();
        builder.Add("Prepare", module.Prepare);
        JobGraph<GraphInput> graph = builder.Build();
        var input = new GraphInput(new GraphData(1));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Equal(CommandEnqueueStatus.NotRunning, runtime.Schedule(graph, in input, out JobRun? run, canceled.Token));
        Assert.Null(run);
        Assert.Throws<InvalidOperationException>(() => runtime.TrySchedule(graph, in input, out _));
        Assert.Throws<OperationCanceledException>(() => runtime.TrySchedule(graph, in input, out _, token: canceled.Token));
        CompletedResult.Read(runtime.StartAsync());
        Assert.Equal(CommandEnqueueStatus.Canceled, runtime.Schedule(graph, in input, out run, cancellationToken: canceled.Token));
        Assert.Null(run);
        Assert.Equal(CommandEnqueueStatus.Accepted, runtime.Schedule(graph, in input, out run));
        Assert.Equal(CommandEnqueueStatus.QueueFull, runtime.Schedule(graph, in input, out JobRun? rejected));
        Assert.Null(rejected);
        for (int attempt = 0; attempt < 100 && !run!.IsCompleted; attempt++)
        {
            runtime.PumpJobs(16, TimeSpan.FromSeconds(1));
        }

        Assert.True(run!.IsCompleted);
        run.ThrowIfFailed();
        run.Dispose();
        if (runtime.TrySchedule(graph, in input, out JobRun? legacy))
        {
            for (int attempt = 0; attempt < 100 && !legacy.IsCompleted; attempt++)
            {
                runtime.PumpJobs(16, TimeSpan.FromSeconds(1));
            }

            Assert.True(legacy.IsCompleted);
            legacy.ThrowIfFailed();
            legacy.Dispose();
        }
        else
        {
            Assert.Fail("Disposing the prior graph should return capacity.");
        }

        CompletedResult.Read(runtime.StopAsync());
        Assert.Equal(CommandEnqueueStatus.NotRunning, runtime.Schedule(graph, in input, out run));
        Assert.Null(run);
    }

    /// <summary>Async and legacy workflow names share cancellation, scheduling and result ownership.</summary>
    [Fact]
    public void WorkflowAsyncAliasPreservesExecutionAndCancellation()
    {
        using var fixture = new WorkflowFixture(options: new ExecutionOptions { MaxInFlightRuns = 1 });
        fixture.Module.Handler = (value, _) => ValueTask.FromResult(value * 3);
        Assert.Equal(15, fixture.Wait(fixture.Module.Port.ExecuteAsync(5, cancellationToken: default)));
        Assert.Equal(18, fixture.Wait(fixture.Module.Port.Execute(6, token: default)));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        int command = 7;
        Assert.Equal(CommandEnqueueStatus.Canceled, fixture.Module.Port.Enqueue(in command, out IValueTaskSource<int>? source, canceled.Token));
        Assert.Null(source);
        Assert.Equal(CommandEnqueueStatus.Accepted, fixture.Module.Port.Enqueue(in command, out source));
        var result = new ValueTask<int>(source!, 0);
        Assert.Equal(CommandEnqueueStatus.QueueFull, fixture.Module.Port.Enqueue(in command, out source));
        Assert.Null(source);
        Assert.Equal(21, fixture.Wait(result));
        Assert.ThrowsAny<OperationCanceledException>(() => CompletedResult.Read(fixture.Module.Port.ExecuteAsync(8, canceled.Token)));
    }
}
