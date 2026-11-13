using System.Reflection;
using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Exercises the actual queue route with managed native-boundary failures and no GPU.</summary>
public sealed class PartialSubmissionTests
{
    /// <summary>Retains only successfully issued dependencies when a later operation and recovery marker fail.</summary>
    /// <param name="validation">Whether managed diagnostics are enabled.</param>
    /// <param name="commands">Whether the submission includes a real managed command object.</param>
    /// <param name="failure">The native operation to fail.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(true, true, "wait")]
    [InlineData(false, true, "wait")]
    [InlineData(true, false, "wait")]
    [InlineData(false, false, "wait")]
    [InlineData(true, true, "signal")]
    [InlineData(false, true, "signal")]
    [InlineData(true, false, "signal")]
    [InlineData(false, false, "signal")]
    [InlineData(true, true, "completion")]
    [InlineData(false, true, "completion")]
    [InlineData(true, false, "completion")]
    [InlineData(false, false, "completion")]
    public async Task PartialFailureRetainsIssuedOperationsUntilDeviceRemovalAsync(bool validation, bool commands, string failure)
    {
        var driver = new FakeQueueDriver();
        using var device = new Direct3D12Device(driver, validation);
        Direct3D12Semaphore[] waits = CreateSemaphores(device, 3);
        Direct3D12Semaphore[] signals = CreateSemaphores(device, 3);
        IGraphicsSubmission producer = device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = waits });
        nint producerFence = driver.Created[^1];
        Direct3D12CommandBuffer? command = commands ? CreateCommand(device) : null;
        nint[] expectedCommands = command == null ? [] : [command.Native];
        int producerSignals = driver.Signals.Count;
        driver.Failure = failure;
        driver.FailAt = failure == "completion" ? 1 : 2;
        driver.FailRecovery = true;
        var desc = new QueueSubmitDesc
        {
            CommandBuffers = command == null ? [] : [command],
            WaitSemaphores = waits.Select(Wait).ToArray(),
            SignalSemaphores = signals,
        };

        Assert.Same(driver.PrimaryError, Assert.Throws<InvalidOperationException>(() => device.Queue.Submit(desc)));
        Direct3D12Submission failed = Assert.IsType<Direct3D12Submission>(device.FailedSubmission);
        nint recoveryFence = driver.Created[^1];
        Assert.True(failed.HasNativeUse);
        Assert.Null(failed.NextFailed);
        Assert.Same(driver.RecoveryError, failed.RecoveryError);
        Assert.NotSame(driver.PrimaryError, failed.RecoveryError);
        Assert.Equal(failure == "wait" ? 2 : 3, driver.WaitAttempts);
        Assert.Equal(failure == "wait" ? 1 : 3, driver.Waits.Count);
        Assert.Equal(failure == "wait" ? 0 : failure == "signal" ? 2 : 3, driver.SignalAttempts);
        Assert.Equal(producerSignals + (failure == "wait" ? 0 : failure == "signal" ? 1 : 3), driver.Signals.Count);
        Assert.Equal(failure == "wait" || command == null ? 0 : 1, driver.Executions.Count);
        if (failure != "wait" && command != null)
        {
            Assert.Equal(expectedCommands, Assert.Single(driver.Executions));
            Assert.Equal(CommandBufferState.Submitted, command.State);
        }

        for (int i = 0; i < waits.Length; i++)
        {
            bool consumed = failure != "wait" || i == 0;
            Assert.Equal(consumed ? 2UL : 1UL, waits[i].WaitValue);
            Assert.Equal(2UL, waits[i].SignalValue);
            AssertSignalState(device, waits[i], !consumed);
            bool signaled = failure == "completion" || (failure == "signal" && i == 0);
            Assert.Equal(signaled ? 2UL : 1UL, signals[i].SignalValue);
            Assert.Equal(1UL, signals[i].WaitValue);
            AssertSignalState(device, signals[i], signaled);
        }

        Assert.Equal(waits.Take(failure == "wait" ? 1 : 3).Select(semaphore => (semaphore.Native, 1UL)), driver.Waits);
        Assert.Equal(signals.Take(failure == "wait" ? 0 : failure == "signal" ? 1 : 3).Select(semaphore => (semaphore.Native, 1UL)), driver.Signals.Skip(producerSignals));

        // Retire the producer first: its ownership must not conceal the partial consumer's use.
        driver.Complete(producerFence);
        await producer.WaitAsync();
        producer.Dispose();
        if (validation)
        {
            Assert.Throws<InvalidOperationException>(() => waits[0].Dispose());
            if (failure != "wait")
            {
                Assert.Throws<InvalidOperationException>(() => signals[0].Dispose());
            }

            Assert.Throws<InvalidOperationException>(() => device.Validation!.PrepareSubmission(new QueueSubmitDesc()));
        }

        Assert.Throws<InvalidOperationException>(() => device.Dispose());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => device.WaitForRetirementAsync(canceled.Token).AsTask());
        device.RetireCompletedFailedSubmissions();
        Assert.Same(failed, device.FailedSubmission);
        Assert.DoesNotContain(recoveryFence, driver.Released);
        Assert.Equal(0UL, driver.Completed(recoveryFence));
        Assert.DoesNotContain(recoveryFence, driver.CompletionSignals);
        Assert.Empty(driver.CompletionWaits);

        // No marker was ever issued: only confirmed native removal permits retirement.
        driver.Removed = unchecked((int)0x887A0005);
        await device.WaitForRetirementAsync();
        Assert.Null(device.FailedSubmission);
        Assert.Equal(SubmissionStatus.Disposed, failed.Status);
        Assert.Equal(1, driver.Released.Count(value => value == recoveryFence));
        Assert.Equal(0UL, driver.Completed(recoveryFence));
        if (command != null)
        {
            Assert.Equal(failure == "wait" ? CommandBufferState.Executable : CommandBufferState.Faulted, command.State);
            ReleaseCommandTracker(device, command);
        }

        foreach (Direct3D12Semaphore semaphore in waits.Concat(signals))
        {
            semaphore.Dispose();
        }
    }

    /// <summary>Abandons a submission that failed before any native operation without consuming binary state.</summary>
    /// <param name="validation">Whether managed diagnostics are enabled.</param>
    /// <param name="failure">The first operation to fail.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(true, "wait")]
    [InlineData(false, "wait")]
    [InlineData(true, "signal")]
    [InlineData(false, "signal")]
    [InlineData(true, "execute")]
    [InlineData(false, "execute")]
    public async Task FirstOperationFailureAbandonsUnissuedSubmissionAsync(bool validation, string failure)
    {
        var driver = new FakeQueueDriver();
        using var device = new Direct3D12Device(driver, validation);
        var semaphore = (Direct3D12Semaphore)device.CreateSemaphore();
        if (failure == "wait")
        {
            IGraphicsSubmission producer = device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = [semaphore] });
            driver.Complete(driver.Created[^1]);
            await producer.WaitAsync();
            producer.Dispose();
        }

        Direct3D12CommandBuffer? command = failure == "execute" ? CreateCommand(device) : null;
        driver.Failure = failure;
        driver.FailAt = 1;
        driver.FailRecovery = true;
        var desc = new QueueSubmitDesc
        {
            CommandBuffers = command == null ? [] : [command],
            WaitSemaphores = failure == "wait" ? [Wait(semaphore)] : [],
            SignalSemaphores = failure == "signal" ? [semaphore] : [],
        };
        int completionAttempts = driver.CompletionAttempts;
        Assert.Same(driver.PrimaryError, Assert.Throws<InvalidOperationException>(() => device.Queue.Submit(desc)));
        nint unusedFence = driver.Created[^1];
        Assert.Null(device.FailedSubmission);
        Assert.Equal(1, driver.Released.Count(value => value == unusedFence));
        Assert.Equal(completionAttempts, driver.CompletionAttempts);
        Assert.Empty(driver.Waits);
        Assert.Empty(driver.Executions);
        Assert.Equal(failure == "wait" ? 1 : 0, driver.WaitAttempts);
        Assert.Equal(failure == "signal" ? 1 : 0, driver.SignalAttempts);
        Assert.Equal(failure == "execute" ? 1 : 0, driver.ExecuteAttempts);
        Assert.Equal(failure == "wait" ? 1 : 0, driver.Signals.Count);
        Assert.Equal(1UL, semaphore.WaitValue);
        Assert.Equal(failure == "wait" ? 2UL : 1UL, semaphore.SignalValue);
        AssertSignalState(device, semaphore, failure == "wait");
        device.Validation?.PrepareSubmission(new QueueSubmitDesc());
        if (command != null)
        {
            Assert.Equal(CommandBufferState.Executable, command.State);
            ReleaseCommandTracker(device, command);
        }

        semaphore.Dispose();
    }

    /// <summary>Retires a successful recovery marker only after actual completion, not cancellation of its waiter.</summary>
    /// <param name="validation">Whether managed diagnostics are enabled.</param>
    /// <param name="commands">Whether a command accompanies the semaphore operations.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task RecoveryMarkerWaitCancellationDoesNotRetireNativeUseAsync(bool validation, bool commands)
    {
        var driver = new FakeQueueDriver();
        using var device = new Direct3D12Device(driver, validation);
        Direct3D12Semaphore[] signals = CreateSemaphores(device, 2);
        Direct3D12CommandBuffer? command = commands ? CreateCommand(device) : null;
        driver.Failure = "signal";
        driver.FailAt = 2;
        Assert.Same(driver.PrimaryError, Assert.Throws<InvalidOperationException>(() => device.Queue.Submit(new QueueSubmitDesc
        {
            CommandBuffers = command == null ? [] : [command],
            SignalSemaphores = signals,
        })));
        Direct3D12Submission failed = Assert.IsType<Direct3D12Submission>(device.FailedSubmission);
        nint recoveryFence = driver.Created[^1];
        Assert.Null(failed.RecoveryError);
        Assert.Contains(recoveryFence, driver.CompletionSignals);
        using var canceled = new CancellationTokenSource();
        Task waiting = device.WaitForRetirementAsync(canceled.Token).AsTask();
        Assert.False(waiting.IsCompleted);
        Assert.Equal(recoveryFence, Assert.Single(driver.CompletionWaits));
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        device.RetireCompletedFailedSubmissions();
        Assert.Same(failed, device.FailedSubmission);
        Assert.Equal(SubmissionStatus.Pending, failed.Status);
        Assert.DoesNotContain(recoveryFence, driver.Released);
        if (validation)
        {
            Assert.Throws<InvalidOperationException>(() => signals[0].Dispose());
        }

        Task survivor = device.WaitForRetirementAsync().AsTask();
        Assert.False(survivor.IsCompleted);
        Assert.Single(driver.CompletionWaits);
        driver.Complete(recoveryFence);
        await survivor.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(device.FailedSubmission);
        Assert.Equal(SubmissionStatus.Disposed, failed.Status);
        Assert.Equal(1, driver.Released.Count(value => value == recoveryFence));
        if (command != null)
        {
            Assert.Equal(CommandBufferState.Completed, command.State);
            ReleaseCommandTracker(device, command);
        }

        foreach (Direct3D12Semaphore semaphore in signals)
        {
            semaphore.Dispose();
        }
    }

    /// <summary>Native removal probes preserve original HRESULTs even after the cached device state is lost.</summary>
    /// <param name="validation">Whether diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LossDoesNotReplaceNativeErrorWithReadinessRejection(bool validation)
    {
        var driver = new FakeQueueDriver { Removed = unchecked((int)0x887A0005) };
        using var device = new Direct3D12Device(driver, validation);
        const int OriginalFailure = unchecked((int)0x80004005);
        System.Runtime.InteropServices.COMException first = Assert.Throws<System.Runtime.InteropServices.COMException>(() => device.Check(OriginalFailure));
        Assert.Equal(OriginalFailure, first.HResult);
        Assert.Equal(GraphicsDeviceStatus.Lost, device.Status);
        System.Runtime.InteropServices.COMException repeated = Assert.Throws<System.Runtime.InteropServices.COMException>(() => device.Check(OriginalFailure));
        Assert.Equal(OriginalFailure, repeated.HResult);
        Assert.Equal((nint)0, device.QueueNative);
        Assert.Throws<InvalidOperationException>(() => _ = device.Native);
        device.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = device.QueueNative);
    }

    private static Direct3D12Semaphore[] CreateSemaphores(Direct3D12Device device, int count)
        => Enumerable.Range(0, count).Select(_ => (Direct3D12Semaphore)device.CreateSemaphore()).ToArray();

    private static SemaphoreWaitDesc Wait(Direct3D12Semaphore semaphore)
        => new() { Semaphore = semaphore, Stages = PipelineStage.AllCommands };

    private static void AssertSignalState(Direct3D12Device device, Direct3D12Semaphore semaphore, bool signaled)
    {
        if (device.Validation is not { } validation)
        {
            return;
        }

        if (signaled)
        {
            validation.PresentationWaitValidate([semaphore]);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => validation.PresentationWaitValidate([semaphore]));
        }
    }

    private static Direct3D12CommandBuffer CreateCommand(Direct3D12Device device)
    {
        // Deliberately bypass native allocation. Never call Dispose on this numeric-handle command.
        var command = (Direct3D12CommandBuffer)RuntimeHelpers.GetUninitializedObject(typeof(Direct3D12CommandBuffer));
        SetField(command, "_owner", device);
        var transfers = new ShaderDataTransferState();
        if (device.Validation is { } validation)
        {
            transfers.ConfigureValidation(validation, command);
            validation.Register(command, device);
        }

        SetField(command, "_shaderData", transfers);
        SetField(command, "<Native>k__BackingField", (nint)10000);
        SetField(command, "<State>k__BackingField", CommandBufferState.Executable);
        return command;
    }

    private static void SetField(Direct3D12CommandBuffer command, string name, object value)
        => typeof(Direct3D12CommandBuffer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(command, value);

    private static void ReleaseCommandTracker(Direct3D12Device device, Direct3D12CommandBuffer command)
    {
        Assert.NotEqual(CommandBufferState.Submitted, command.State);
        device.Validation?.Discard(command);
        device.Validation?.Release(command);
    }

    private sealed class FakeQueueDriver : IDirect3D12QueueDriver
    {
        private readonly Dictionary<nint, ulong> _completed = [];
        private readonly Dictionary<nint, TaskCompletionSource> _completion = [];
        private int _operationAttempts;
        private bool _primaryThrown;

        internal InvalidOperationException PrimaryError { get; } = new("primary native failure");

        internal InvalidOperationException RecoveryError { get; } = new("secondary recovery failure");

        internal string? Failure { get; set; }

        internal int FailAt { get; set; }

        internal bool FailRecovery { get; set; }

        internal int Removed { get; set; }

        internal int WaitAttempts { get; private set; }

        internal int SignalAttempts { get; private set; }

        internal int CompletionAttempts { get; private set; }

        internal int ExecuteAttempts { get; private set; }

        internal List<nint> Created { get; } = [];

        internal List<nint> Released { get; } = [];

        internal List<(nint Fence, ulong Value)> Waits { get; } = [];

        internal List<(nint Fence, ulong Value)> Signals { get; } = [];

        internal List<nint[]> Executions { get; } = [];

        internal List<nint> CompletionSignals { get; } = [];

        internal List<nint> CompletionWaits { get; } = [];

        public void CreateFence(Direct3D12Device owner, out nint fence)
        {
            fence = Created.Count + 1;
            Created.Add(fence);
            _completed.Add(fence, 0);
            _completion.Add(fence, new(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        public void Wait(Direct3D12Device owner, nint fence, ulong value)
        {
            WaitAttempts++;
            Fail("wait");
            Waits.Add((fence, value));
        }

        public void Signal(Direct3D12Device owner, nint fence, ulong value)
        {
            // Setup producer signals precede fault arming and are excluded from attempt counts.
            if (Failure != null)
            {
                SignalAttempts++;
            }

            Fail("signal");
            Signals.Add((fence, value));
        }

        public void Execute(Direct3D12Device owner, ReadOnlySpan<nint> commands)
        {
            ExecuteAttempts++;
            Fail("execute");
            Executions.Add(commands.ToArray());
        }

        public void SignalCompletion(Direct3D12Device owner, nint fence)
        {
            CompletionAttempts++;
            if (_primaryThrown && FailRecovery)
            {
                throw RecoveryError;
            }

            Fail("completion");
            CompletionSignals.Add(fence);
        }

        public ulong Completed(nint fence) => _completed[fence];

        public int DeviceRemoved(Direct3D12Device owner) => Removed;

        public Task WaitForCompletion(Direct3D12Device owner, nint fence)
        {
            CompletionWaits.Add(fence);
            return _completion[fence].Task;
        }

        public void Release(nint fence) => Released.Add(fence);

        internal void Complete(nint fence)
        {
            _completed[fence] = 1;
            _completion[fence].TrySetResult();
        }

        private void Fail(string operation)
        {
            if (Failure == operation && ++_operationAttempts == FailAt)
            {
                _primaryThrown = true;
                throw PrimaryError;
            }
        }
    }
}
