using System.IO.Pipes;
using Lumyte.Execution;
using Lumyte.UI.Application.WebView.Cef;
using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Backend-private workflow ownership without CEF, Python, or native prerequisites.</summary>
public sealed class CefExecutionTests
{
    /// <summary>Entry and asynchronous I/O resumption use the same execution context, not the caller's context.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task IoResumesThroughExecutionSchedulerAsync()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string pipeName = "lumyte-cef-test-" + Guid.NewGuid().ToString("N");
        using var connectionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var reader = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var writer = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        Task connect = writer.ConnectAsync(connectionDeadline.Token);
        await reader.WaitForConnectionAsync(connectionDeadline.Token);
        await connect;

        // Preparation and startup require the same physical host thread. Pipe connection
        // can resume on another thread, even when the caller has a synchronization context.
        var scope = new CefExecutionScope();
        using var stop = new CancellationTokenSource();
        SynchronizationContext? caller = SynchronizationContext.Current;
        SynchronizationContext? workflow = null;
        Task root = scope.ExecuteAsync(
            async () =>
            {
                workflow = SynchronizationContext.Current;
                byte[] packet = new byte[1];
                ValueTask read = reader.ReadExactlyAsync(packet, stop.Token);
                Assert.False(read.IsCompleted);
                entered.SetResult();
                await read;
                Assert.Equal(42, packet[0]);
                Assert.NotNull(workflow);
                Assert.NotSame(caller, workflow);
                Assert.Same(workflow, SynchronizationContext.Current);
            },
            stop.Token);
        Exception? failure = null;
        try
        {
            // Observe a startup/workflow fault immediately rather than hiding it behind
            // the entry timeout (and then an expired peer-write cancellation token).
            await await Task.WhenAny(entered.Task, root).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(entered.Task.IsCompletedSuccessfully);
            Assert.False(root.IsCompleted);
            using var writeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await writer.WriteAsync(new byte[] { 42 }, writeDeadline.Token);
            await root.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            stop.Cancel();
            try
            {
                try
                {
                    await root.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                }
                finally
                {
                    await scope.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception) when (failure is not null)
            {
                // Cleanup must still join and stop, but never replace the original failure.
            }
        }
    }

    /// <summary>Waiting I/O retains the single session reservation; it is not one command per packet.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task SuspendedSessionKeepsBoundedReservationAsync()
    {
        var scope = new CefExecutionScope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task root = scope.ExecuteAsync(
            async () =>
            {
                entered.SetResult();
                await input.Task;
            },
            CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<ExecutionQueueFullException>(() => scope.ExecuteAsync(() => Task.CompletedTask, CancellationToken.None));
        }
        finally
        {
            input.TrySetResult();
            await root.WaitAsync(TimeSpan.FromSeconds(5));
            await scope.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>Started I/O is explicitly canceled and joined before stopping the runtime.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task SessionCancellationPrecedesRuntimeStopAsync()
    {
        var scope = new CefExecutionScope();
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool joined = false;
        Task root = scope.ExecuteAsync(
            async () =>
            {
                entered.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                }
                finally
                {
                    joined = true;
                }
            },
            stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            stop.Cancel();
            await root.WaitAsync(TimeSpan.FromSeconds(5));
            await scope.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.True(joined);
    }

    /// <summary>A preclosed session is never admitted or launched, and the unused runtime stops normally.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CloseBeforeRuntimeStartDoesNotRunSessionAsync()
    {
        var scope = new CefExecutionScope();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        bool launched = false;
        try
        {
            await scope.ExecuteAsync(
                () =>
                {
                    launched = true;
                    return Task.CompletedTask;
                },
                stop.Token).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await scope.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.False(launched);
    }

    /// <summary>A failed workflow releases its reservation after observation, without losing runtime ownership.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task FailedRootIsObservedBeforeRuntimeStopAsync()
    {
        var scope = new CefExecutionScope();
        try
        {
            await Assert.ThrowsAsync<IOException>(() => scope.ExecuteAsync(() => Task.FromException(new IOException("fixture")), CancellationToken.None));
            await scope.ExecuteAsync(() => Task.CompletedTask, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await scope.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>The existing direct constructor owns and joins its private runtime even when no helper exists.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task DirectSessionFaultStillJoinsOwnedRuntimeAsync()
    {
        using var session = new CefHelperSession(new("http://127.0.0.1/"), new(1024), () => { }, new());
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(session.IsClosed);
        Assert.True(session.TryTakeSnapshot(out HelperSnapshot snapshot));
        Assert.Equal(UiWebViewState.Faulted, snapshot.State);
        Assert.Equal("cef-helper-unavailable", snapshot.FailureCode);
    }

    /// <summary>Missing native supervision faults only the browser, with no compilation or fallback launch.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task MissingSupervisorFaultsLocallyAsync()
    {
        string helper = Path.GetTempFileName();
        try
        {
            using var session = new CefHelperSession(new("http://127.0.0.1/"), new(1024), () => { }, new()
            {
                HelperExecutablePath = helper,
                SupervisorExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "lumyte-cef-supervisor"),
            });
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(session.IsClosed);
            Assert.True(session.TryTakeSnapshot(out HelperSnapshot snapshot));
            Assert.Equal(UiWebViewState.Faulted, snapshot.State);
            Assert.Equal(OperatingSystem.IsLinux() ? "cef-helper-supervisor-unavailable" : "cef-helper-platform-unsupported", snapshot.FailureCode);
        }
        finally
        {
            File.Delete(helper);
        }
    }
}
