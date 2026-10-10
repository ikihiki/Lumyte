using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Checks caller-selected binary GPU waits and signals exclusively through common APIs.</summary>
public static class SemaphoreExercise
{
    /// <summary>Issues semaphore-only queue work and checks validation without a CPU wait between dependent submissions.</summary>
    /// <param name="device">The existing device.</param>
    /// <returns>The verification report.</returns>
    public static async Task<string> RunAsync(IGraphicDevice device)
    {
        using IGraphicsSemaphore first = device.CreateSemaphore();
        using IGraphicsSemaphore second = device.CreateSemaphore();
        Expect<ArgumentException>(() => device.Queue.Submit(new QueueSubmitDesc()));
        Expect<InvalidOperationException>(() => device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = first, Stages = PipelineStage.AllCommands }] }));
        using IGraphicsSubmission signaled = device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = [first] });
        Expect<InvalidOperationException>(() => device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = [first] }));
        Expect<ArgumentException>(() => device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = [second, second] }));
        Expect<ArgumentException>(() => device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = first, Stages = PipelineStage.None }] }));
        Expect<ArgumentException>(() => device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = first, Stages = PipelineStage.Host }] }));
        Expect<ArgumentException>(() => device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = first, Stages = (PipelineStage)1024 }] }));
        Expect<ArgumentException>(() => device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = first, Stages = PipelineStage.Copy }, new() { Semaphore = first, Stages = PipelineStage.Copy }] }));
        Expect<ArgumentException>(() => device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = first, Stages = PipelineStage.Copy }], SignalSemaphores = [first] }));
        using IGraphicsSubmission forwarded = device.Queue.Submit(new QueueSubmitDesc
        {
            WaitSemaphores = [new() { Semaphore = first, Stages = PipelineStage.AllCommands }],
            SignalSemaphores = [second],
        });
        using IGraphicsSubmission consumed = device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = second, Stages = PipelineStage.Copy }] });
        Expect<InvalidOperationException>(() => device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = second, Stages = PipelineStage.Copy }] }));
        await consumed.WaitAsync();
        await forwarded.WaitAsync();
        await signaled.WaitAsync();
        using IGraphicsSubmission reusedSignal = device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = [first] });
        using IGraphicsSubmission reusedWait = device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = first, Stages = PipelineStage.AllCommands }] });
        await reusedWait.WaitAsync();
        await reusedSignal.WaitAsync();
        first.Dispose();
        first.Dispose();
        Expect<ObjectDisposedException>(() => device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = [first] }));
        return "Semaphore checks passed: explicit signal/wait chains, semaphore-only submissions, stages, duplicate and consumed signals, reuse and lifetime.";
    }

    /// <summary>Checks rejection before any foreign-device semaphore reaches a native queue.</summary>
    /// <param name="device">The submitting device.</param>
    /// <param name="foreign">The other semaphore owner.</param>
    public static void CheckForeignDevice(IGraphicDevice device, IGraphicDevice foreign)
    {
        using IGraphicsSemaphore semaphore = foreign.CreateSemaphore();
        Expect<ArgumentException>(() => device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = [semaphore] }));
        Expect<ArgumentException>(() => device.Queue.Submit(new QueueSubmitDesc { WaitSemaphores = [new() { Semaphore = semaphore, Stages = PipelineStage.Copy }] }));
    }

    private static void Expect<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
