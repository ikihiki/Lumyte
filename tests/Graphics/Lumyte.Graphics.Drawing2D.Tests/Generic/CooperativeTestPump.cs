using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lumyte.Execution;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal static class CooperativeTestPump
{
    internal static void Complete(ExecutionRuntime runtime, Task task, [CallerArgumentExpression(nameof(task))] string operation = "")
    {
        void Progress()
        {
            runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
        }

        Complete(Progress, task, TimeSpan.FromSeconds(15), operation);
    }

    internal static void Complete(Action progress, Task task, TimeSpan timeout, string operation)
    {
        var deadline = Stopwatch.StartNew();
        int owner = Environment.CurrentManagedThreadId;
        while (!task.IsCompleted)
        {
            progress();
            if (!task.IsCompleted && deadline.Elapsed >= timeout)
            {
                throw new TimeoutException($"{operation} did not complete within {timeout}; task status={task.Status}, owner thread={owner}.");
            }

            // Stay on the cooperative owner while allowing external continuations to run.
            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }
}
