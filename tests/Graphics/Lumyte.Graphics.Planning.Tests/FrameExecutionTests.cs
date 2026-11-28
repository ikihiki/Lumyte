using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Checks the frame-level one-shot execution and retained recovery owners.</summary>
public sealed class FrameExecutionTests
{
    /// <summary>Every completed invocation releases its frame before a fresh declaration.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task SuccessRetiresAndAllowsFreshFramesAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        for (int i = 0; i < 3; i++)
        {
            GraphicsFrameScope frame = session.CreateFrame();
            DeclareCopy(frame);
            await frame.ExecuteAsync();
            Assert.Throws<InvalidOperationException>(() => frame.ExecuteAsync());
            Assert.Throws<InvalidOperationException>(() => frame.CompileAsync());
            Assert.Equal(0, session.Value.ActiveExecutionCount);
            Assert.Equal(0, session.Value.ActiveFrameCount);
        }

        Assert.Equal(3, device.Events.Count(e => e == "submit"));
        Assert.Equal(3, device.Events.Count(e => e == "wait"));
        Assert.Equal(3, device.Events.Count(e => e == "dispose-commands"));
        Assert.True(device.Events.IndexOf("wait") < device.Events.IndexOf("dispose-commands"));
    }

    /// <summary>Pre-cancellation consumes both the integrated and low-level compilation entry.</summary>
    /// <param name="manual">Whether the first attempt uses low-level compilation.</param>
    /// <returns>The asynchronous assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCanceledAttemptIsConsumedAsync(bool manual)
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (manual)
            {
                await frame.Value.CompileAsync(cancellation.Token);
            }
            else
            {
                await frame.Value.ExecuteAsync(cancellation.Token);
            }
        });
        Assert.Throws<InvalidOperationException>(() => frame.Value.ExecuteAsync());
        Assert.Throws<InvalidOperationException>(() => frame.Value.CompileAsync());
        Assert.Empty(device.Events);
    }

    /// <summary>Manual compilation cannot be bypassed by the convenience sequence.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task ManualCompilationConsumesExecuteAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        Assert.Throws<InvalidOperationException>(() => frame.Value.ExecuteAsync());
        Assert.Empty(device.Events);
    }

    /// <summary>Invalid exports fail compilation without opening a retry path.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task CompileFailureConsumesFrameAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        GraphicsFrameScope frame = session.CreateFrame();
        frame.Plan.Export(frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopyDestination }));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await frame.ExecuteAsync());
        Assert.Throws<InvalidOperationException>(() => frame.ExecuteAsync());
        Assert.Throws<InvalidOperationException>(() => frame.CompileAsync());
        Assert.Equal(0, session.Value.ActiveFrameCount);
        Assert.Empty(device.Events);
    }

    /// <summary>Failed preparation retires its private compiled owner without exposing it.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task PrepareFailureRetiresFrameAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device, new() { MaximumAllocationBytes = 1 });
        GraphicsFrameScope frame = session.CreateFrame();
        DeclareCopy(frame);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await frame.ExecuteAsync());
        Assert.Throws<InvalidOperationException>(() => frame.ExecuteAsync());
        Assert.Equal(0, session.Value.ActiveFrameCount);
        Assert.Empty(device.Events);
    }

    /// <summary>Unknown acceptance retains hidden owners until device retirement, never resubmitting.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task UnknownSubmissionRetainsOwnersForRecoveryAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice { UnknownSubmission = true };
        await using var session = new SessionOwner(host.Module, device);
        GraphicsFrameScope frame = session.CreateFrame();
        DeclareCopy(frame);
        InvalidOperationException primary = await Assert.ThrowsAsync<InvalidOperationException>(async () => await frame.ExecuteAsync());
        Assert.Equal("Acceptance unknown.", primary.Message);
        Assert.Throws<InvalidOperationException>(() => frame.ExecuteAsync());
        Assert.Throws<InvalidOperationException>(() => session.CreateFrame());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await frame.RetireAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DrainAsync());
        Assert.DoesNotContain("dispose-commands", device.Events);
        device.Status = GraphicsDeviceStatus.Lost;
        await session.RecoverRetiredDeviceAsync();
        await frame.RetireAsync();
        Assert.Single(device.Events, e => e == "submit");
        Assert.Equal(0, session.Value.ActiveFrameCount);
        Assert.Equal(0, session.Value.ActiveExecutionCount);
    }

    /// <summary>Canceling after submit retains owners for drain, without resubmission.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task CanceledWaitCanDrainAndRetireFrameAsync()
    {
        await using var host = new PlanningHost();
        using var cancellation = new CancellationTokenSource();
        var device = new ManagedDevice { OnSubmit = cancellation.Cancel };
        await using var session = new SessionOwner(host.Module, device);
        GraphicsFrameScope frame = session.CreateFrame();
        DeclareCopy(frame);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await frame.ExecuteAsync(cancellation.Token));
        Assert.Throws<InvalidOperationException>(() => frame.ExecuteAsync());
        Assert.DoesNotContain("dispose-commands", device.Events);
        Assert.Equal(1, session.Value.ActiveFrameCount);
        await session.DrainAsync();
        await frame.RetireAsync();
        Assert.Single(device.Events, e => e == "submit");
        Assert.Equal(0, session.Value.ActiveFrameCount);
    }

    /// <summary>Concurrent entry and mutation are rejected while a first invocation is active.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task PendingExecutionRejectsConcurrentEntriesAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        GraphicsFrameScope frame = session.CreateFrame();
        DeclareCopy(frame);
        using var submitted = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        device.OnSubmit = () =>
        {
            submitted.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        };
        Task execution = frame.ExecuteAsync().AsTask();
        try
        {
            Assert.True(submitted.Wait(TimeSpan.FromSeconds(10)));
            Assert.Throws<InvalidOperationException>(() => frame.ExecuteAsync());
            Assert.Throws<InvalidOperationException>(() => frame.CompileAsync());
            Assert.Throws<InvalidOperationException>(() => frame.Plan.AddPass("late"));
        }
        finally
        {
            release.Set();
        }

        await execution;
        Assert.Single(device.Events, e => e == "submit");
    }

    /// <summary>Cleanup failures retain private owners and never replace the cancellation failure.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task CleanupFailurePreservesPrimaryAndRetainsOwnersAsync()
    {
        await using var host = new PlanningHost();
        using var cancellation = new CancellationTokenSource();
        var device = new ManagedDevice { OnCreateCommands = cancellation.Cancel, CommandDisposeFailures = 8 };
        await using var session = new SessionOwner(host.Module, device);
        GraphicsFrameScope frame = session.CreateFrame();
        DeclareCopy(frame);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await frame.ExecuteAsync(cancellation.Token));
        Assert.Equal(1, session.Value.ActiveFrameCount);
        Assert.DoesNotContain("submit", device.Events);
        Assert.DoesNotContain("dispose-buffer", device.Events);
        Assert.Throws<InvalidOperationException>(() => frame.ExecuteAsync());
        device.CommandDisposeFailures = 0;
        await frame.RetireAsync();
        Assert.Equal(0, session.Value.ActiveFrameCount);
        Assert.Single(device.Events, e => e == "dispose-commands");
        Assert.Single(device.Events, e => e == "dispose-buffer");
    }

    /// <summary>Retirement seals admission before releasing the first imported lease.</summary>
    /// <param name="manual">Whether admission uses manual compilation.</param>
    /// <returns>The asynchronous assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetirementRejectsAdmissionBeforeLeaseReleaseAsync(bool manual)
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        GraphicsFrameScope frame = session.CreateFrame();
        using var lease = new BlockingLease();
        frame.Plan.ImportBuffer<int>(new ManagedBuffer<int>(4, []), lease);
        Task retirement = frame.RetireAsync().AsTask();
        CompiledGraphicsPlan? compiled = null;
        Task? execution = null;
        Exception? rejected = null;
        try
        {
            Assert.True(lease.Entered.Wait(TimeSpan.FromSeconds(10)));
            rejected = await Task.Run(() => Record.Exception(() =>
            {
                if (manual)
                {
                    compiled = frame.CompileAsync().GetAwaiter().GetResult();
                }
                else
                {
                    execution = frame.ExecuteAsync().AsTask();
                }
            })).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(lease.Released);
            Assert.Empty(device.Events);
        }
        finally
        {
            lease.Continue.Set();
        }

        await retirement;
        compiled?.Dispose();
        if (execution is not null)
        {
            await execution;
        }

        Assert.IsType<InvalidOperationException>(rejected);
        Assert.True(lease.Released);
        Assert.Empty(device.Events);
        Assert.Equal(0, session.Value.ActiveFrameCount);
    }

    private static void DeclareCopy(GraphicsFrameScope frame)
    {
        PlanBuffer<int> source = frame.Plan.ImportBuffer<int>(new ManagedBuffer<int>(4, []), new TestLease());
        PlanBuffer<int> target = frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopyDestination });
        PlanPass pass = frame.Plan.AddPass("copy");
        PlanBuffer<int> result = pass.Write(target, target.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        pass.Read(source, source.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        pass.Copy(source, source.Range, result, result.Range);
        frame.Plan.Export(result);
    }

    private sealed class BlockingLease : IDisposable
    {
        internal ManualResetEventSlim Entered { get; } = new();

        internal ManualResetEventSlim Continue { get; } = new();

        internal bool Released { get; private set; }

        public void Dispose()
        {
            if (Released)
            {
                Entered.Dispose();
                Continue.Dispose();
                return;
            }

            Entered.Set();
            if (!Continue.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The retirement test did not release its lease gate.");
            }

            Released = true;
        }
    }
}
