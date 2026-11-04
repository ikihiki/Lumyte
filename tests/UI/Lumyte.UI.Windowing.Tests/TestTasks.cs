using Xunit;

namespace Lumyte.UI.Windowing.Tests;

internal static class TestTasks
{
    internal static T Completed<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "The deterministic frame pump must complete the task before inspection.");
        return task.GetAwaiter().GetResult();
    }
}
