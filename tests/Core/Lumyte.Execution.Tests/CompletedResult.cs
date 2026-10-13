using System.Threading.Tasks.Sources;

namespace Lumyte.Execution.Tests;

// Fixed-host tests pump synchronously. These observers reject incomplete tasks before GetResult.
internal static class CompletedResult
{
    internal static void Read(Task task)
    {
        if (!task.IsCompleted)
        {
            throw new InvalidOperationException("Task must already be complete.");
        }

        task.GetAwaiter().GetResult();
    }

    internal static void Read(ValueTask task)
    {
        if (!task.IsCompleted)
        {
            throw new InvalidOperationException("Task must already be complete.");
        }

        task.GetAwaiter().GetResult();
    }

    internal static TResult Read<TResult>(ValueTask<TResult> task)
    {
        if (!task.IsCompleted)
        {
            throw new InvalidOperationException("Result must already be complete.");
        }

        return task.GetAwaiter().GetResult();
    }

    // The core source is required to reject pending reads synchronously; this also tests that guard.
    internal static TResult ReadSource<TResult>(IValueTaskSource<TResult> source) => source.GetResult(0);
}
