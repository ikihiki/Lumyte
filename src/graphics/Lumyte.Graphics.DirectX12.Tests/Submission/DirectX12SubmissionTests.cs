using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed class DirectX12SubmissionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SuccessfulSubmissionOrdersWaitsCommandsAndCompletionSignals(int waitCount)
    {
        var calls = new SubmissionCalls();
        string[] expected = [.. Enumerable.Range(0, waitCount).Select(index => $"Wait{index}"), "Execute", "Internal", "Caller"];

        DirectX12Submission.Execute(default, waitCount, calls);

        Assert.Equal(expected, calls.Operations);
        Assert.False(calls.Faulted);
    }

    [Theory]
    [InlineData("Wait0", 1)]
    [InlineData("Wait1", 2)]
    [InlineData("Execute", 3)]
    [InlineData("Internal", 4)]
    [InlineData("Caller", 5)]
    public void HandoffFailureFaultsTheBackendAndStopsLaterQueueOperations(string failingOperation, int operationCount)
    {
        using var semaphore = new UnusedSemaphore();
        var completion = new NativeGpuTimelinePoint(semaphore, ulong.MaxValue - 1);
        var cause = new InvalidOperationException("Injected native call failure.");
        var calls = new SubmissionCalls(failingOperation, cause);
        string[] operations = ["Wait0", "Wait1", "Execute", "Internal", "Caller"];

        var failure = Assert.Throws<NativeGpuSubmissionException>(() => DirectX12Submission.Execute(completion, 2, calls));

        Assert.Equal(completion, failure.Completion);
        Assert.Same(cause, failure.InnerException);
        Assert.Equal(operations.Take(operationCount), calls.Operations);
        Assert.True(calls.Faulted);
    }

    private sealed class SubmissionCalls(string? failingOperation = null, Exception? failure = null) : IDirectX12SubmissionCalls
    {
        public List<string> Operations { get; } = [];
        public bool Faulted { get; private set; }
        public void Wait(int index) => Invoke($"Wait{index}");
        public void ExecuteCommands() => Invoke("Execute");
        public void SignalInternal() => Invoke("Internal");
        public void SignalCaller() => Invoke("Caller");
        public void Fault() => Faulted = true;

        private void Invoke(string operation)
        {
            Operations.Add(operation);
            if (operation == failingOperation) { throw failure!; }
        }
    }

    private sealed class UnusedSemaphore : NativeGpuSemaphore
    {
        public override bool IsComplete(ulong value) => throw new InvalidOperationException("No completion query is expected.");
        public override void WaitCpu(ulong value) => throw new InvalidOperationException("No wait is expected.");
        public override void SignalCpu(ulong value) => throw new InvalidOperationException("No signal is expected.");
        public override void Dispose() { }
    }
}
