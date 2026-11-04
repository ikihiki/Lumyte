using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks submit serialization is independent from bounded GPU completion ownership.</summary>
public sealed class FrameQueueTests
{
    /// <summary>Checks a pending frame releases submit access but continues consuming an in-flight slot.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task SubmittedFramesAllowAnotherWindowUntilTheBudgetIsFullAsync()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var queue = new UiFrameSubmissionQueue(gate, 2);
        using UiFrameSubmissionQueue.Flight first = await queue.EnterAsync();
        Task<UiFrameSubmissionQueue.Flight> secondTask = queue.EnterAsync().AsTask();
        Assert.False(secondTask.IsCompleted);
        first.Submitted();
        using UiFrameSubmissionQueue.Flight second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        second.Submitted();
        Task<UiFrameSubmissionQueue.Flight> thirdTask = queue.EnterAsync().AsTask();
        Assert.False(thirdTask.IsCompleted);
        first.Dispose();
        using UiFrameSubmissionQueue.Flight third = await thirdTask.WaitAsync(TimeSpan.FromSeconds(5));
        third.Submitted();
        await second.ResumeAsync();
        Assert.Equal(0, gate.CurrentCount);
        second.Dispose();
        Assert.Equal(1, gate.CurrentCount);
    }

    /// <summary>Checks abandoning preparation returns both reservations without submitting.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task PreparationFailureReturnsTheBudgetAsync()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var queue = new UiFrameSubmissionQueue(gate, 1);
        UiFrameSubmissionQueue.Flight failed = await queue.EnterAsync();
        failed.Dispose();
        failed.Dispose();
        using UiFrameSubmissionQueue.Flight retry = await queue.EnterAsync();
        Assert.Equal(0, gate.CurrentCount);
    }
}
