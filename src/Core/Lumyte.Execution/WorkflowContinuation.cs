using System.Threading.Tasks.Sources;

namespace Lumyte.Execution;

internal sealed class WorkflowContinuation(SynchronizationContext context, Action<object?> continuation, object? state)
{
    private readonly SynchronizationContext _context = context;
    private readonly Action<object?> _continuation = continuation;
    private readonly object? _state = state;

    internal static void Prepare(ExecutionRuntime runtime, ref Action<object?> continuation, ref object? state, ref ValueTaskSourceOnCompletedFlags flags)
    {
        if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != 0
            && SynchronizationContext.Current is IWorkflowContext workflow
            && workflow.Runtime != runtime)
        {
            state = new WorkflowContinuation(SynchronizationContext.Current, continuation, state);
            continuation = Post;

            // An async source may call captured context.Post while holding its runtime lock.
            // First leave that lock on the ThreadPool, then enter the other runtime's queue.
            flags &= ~ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        }
    }

    private static void Post(object? state)
    {
        var bridge = (WorkflowContinuation)state!;
        bridge._context.Post(Invoke, bridge);
    }

    private static void Invoke(object? state)
    {
        var bridge = (WorkflowContinuation)state!;
        bridge._continuation(bridge._state);
    }
}
