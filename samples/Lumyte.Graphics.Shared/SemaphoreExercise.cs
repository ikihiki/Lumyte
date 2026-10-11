using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Checks caller-selected binary GPU waits and signals exclusively through common APIs.</summary>
public static class SemaphoreExercise
{
    /// <summary>Issues semaphore-only queue work and waits for completion without a CPU wait between dependent submissions.</summary>
    /// <param name="device">The existing device.</param>
    /// <returns>The verification report.</returns>
    public static async Task<string> RunAsync(IGraphicDevice device)
    {
        using IGraphicsSemaphore first = device.CreateSemaphore();
        using IGraphicsSemaphore second = device.CreateSemaphore();
        using IGraphicsSubmission signaled = device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = [first] });
        using IGraphicsSubmission forwarded = device.Queue.Submit(new QueueSubmitDesc
        {
            WaitSemaphores = [new() { Semaphore = first, Stages = PipelineStage.AllCommands }],
            SignalSemaphores = [second],
        });
        using IGraphicsSubmission consumed = device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = second, Stages = PipelineStage.Copy }] });
        await consumed.WaitAsync();
        await forwarded.WaitAsync();
        await signaled.WaitAsync();
        using IGraphicsSubmission reusedSignal = device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = [first] });
        using IGraphicsSubmission reusedWait = device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = first, Stages = PipelineStage.AllCommands }] });
        await reusedWait.WaitAsync();
        await reusedSignal.WaitAsync();
        first.Dispose();
        first.Dispose();
        return "Semaphore checks passed: explicit signal/wait chains, semaphore-only submissions, stages, explicit reuse and completion.";
    }
}
