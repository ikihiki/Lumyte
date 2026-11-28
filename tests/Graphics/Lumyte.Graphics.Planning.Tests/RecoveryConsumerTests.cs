using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Tests explicit completion proofs and retryable cleanup without a native backend.</summary>
public sealed class RecoveryConsumerTests
{
    /// <summary>An acceptance-unknown submission retains all owners until verified device retirement.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task UnknownAcceptanceRequiresRetirementProofAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice { UnknownSubmission = true };
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        AddWork(session, frame);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var preparedOwner = new PreparedOwner(await compiled.PrepareAsync());
        PreparedGraphicsExecution prepared = preparedOwner.Value;
        GraphicsExecution execution = session.CreateExecution(prepared);
        await execution.RecordAsync();
        await execution.FinishAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await execution.SubmitAsync());
        Assert.Equal(GraphicsRecoveryState.Quarantined, execution.Recovery);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await execution.RetireAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Value.RetireAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DrainAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.RecoverRetiredDeviceAsync());
        Assert.DoesNotContain("dispose-commands", device.Events);
        device.Status = GraphicsDeviceStatus.Lost;
        await session.RecoverRetiredDeviceAsync();
        Assert.Equal(GraphicsRecoveryState.Retired, execution.Recovery);
        Assert.True(device.Events.IndexOf("retirement-proof") < device.Events.IndexOf("dispose-commands"));
    }

    /// <summary>Canceling a known wait does not release submitted owners and a later drain can complete.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task CanceledKnownWaitCanBeDrainedAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        AddWork(session, frame);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var preparedOwner = new PreparedOwner(await compiled.PrepareAsync());
        PreparedGraphicsExecution prepared = preparedOwner.Value;
        GraphicsExecution execution = session.CreateExecution(prepared);
        await execution.RecordAsync();
        await execution.FinishAsync();
        await execution.SubmitAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await execution.WaitAsync(cancellation.Token));
        Assert.Equal(GraphicsRecoveryState.Quarantined, execution.Recovery);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await execution.RetireAsync());
        Assert.DoesNotContain("dispose-commands", device.Events);
        await session.DrainAsync();
        Assert.Equal(GraphicsRecoveryState.Retired, execution.Recovery);
        Assert.True(device.Events.IndexOf("wait") < device.Events.IndexOf("dispose-commands"));
        Assert.True(device.Events.IndexOf("dispose-commands") < device.Events.IndexOf("dispose-submission"));
    }

    /// <summary>Failed command disposal retains preparation and is safely retried without resubmission.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task CleanupFailureRetainsOwnersForRetryAsync()
    {
        await using var host = new PlanningHost();
        var device = new ManagedDevice { CommandDisposeFailures = 1 };
        await using var session = new SessionOwner(host.Module, device);
        await using var frame = new FrameOwner(session.CreateFrame());
        AddWork(session, frame);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var preparedOwner = new PreparedOwner(await compiled.PrepareAsync());
        PreparedGraphicsExecution prepared = preparedOwner.Value;
        GraphicsExecution execution = session.CreateExecution(prepared);
        await execution.RecordAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await execution.RetireAsync());
        Assert.Equal(GraphicsRecoveryState.CleanupPending, execution.Recovery);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await prepared.RetireAsync());
        Assert.Throws<InvalidOperationException>(compiled.Dispose);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await frame.Value.RetireAsync());
        Assert.DoesNotContain("dispose-buffer", device.Events);
        await execution.RetireAsync();
        Assert.Equal(GraphicsRecoveryState.Retired, execution.Recovery);
        await execution.RetireAsync();
        Assert.Equal(2, device.Events.Count(e => e == "dispose-commands-attempt"));
        Assert.DoesNotContain("submit", device.Events);
    }

    private static void AddWork(GraphicsDeviceSession session, GraphicsFrameScope frame)
    {
        ComputeKernel<TestArguments> kernel = session.ImportComputeKernel<TestArguments>(new ManagedKernel(), new TestLease());
        PlanBuffer<int> input = frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.ShaderWrite });
        PlanPass pass = frame.Plan.AddPass("work");
        PlanBuffer<int> output = pass.Write(input, new(0, 4), PlanWriteMode.Discard, new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite));
        pass.Dispatch(kernel, new TestArguments(pass.Buffer(output, output.Range, ArgumentBufferAccess.Write)), 1);
        frame.Plan.Export(output);
    }
}
