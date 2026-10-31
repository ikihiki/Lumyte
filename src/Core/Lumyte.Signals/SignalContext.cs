using System.Runtime.ExceptionServices;

namespace Lumyte.Signals;

internal sealed class SignalContext
{
    [ThreadStatic]
    private static SignalContext? _current;
    private static ulong _revision;

    internal static object Gate { get; } = new();

    internal static SignalContext Current => _current ??= new();

    // Accessed only while holding Gate, just like node revisions.
    internal static ulong MutationRevision => _revision;

    internal Dictionary<SignalNode, ulong>? Reads { get; set; }

    internal int Evaluating { get; set; }

    internal int Batching { get; set; }

    internal bool Flushing { get; private set; }

    internal Queue<(SignalEffect Effect, ulong Generation)> Pending { get; } = new();

    internal SignalScope? Scope { get; set; }

    internal static ulong NextRevision() => _revision == ulong.MaxValue ? throw new OverflowException("Signal revision overflow.") : ++_revision;

    internal static void CheckMutation()
    {
        if (Current.Evaluating != 0)
        {
            throw new InvalidOperationException("Signal evaluation must not mutate state or observer lifetimes.");
        }
    }

    internal static void Track(SignalNode node) => Current.Reads?.TryAdd(node, node.Revision);

    internal static bool EqualsValue<T>(IEqualityComparer<T> comparer, T value, T next)
    {
        SignalContext context = Current;
        Dictionary<SignalNode, ulong>? previous = context.Reads;
        context.Reads = null;
        context.Evaluating++;
        try
        {
            return comparer.Equals(value, next);
        }
        finally
        {
            context.Evaluating--;
            context.Reads = previous;
        }
    }

    internal static void ReadUntracked(SignalNode node)
    {
        SignalContext context = Current;
        Dictionary<SignalNode, ulong>? previous = context.Reads;
        context.Reads = null;
        try
        {
            node.ReadTracked();
        }
        finally
        {
            context.Reads = previous;
        }
    }

    internal static ulong GetDependencyRevision(Dictionary<SignalNode, ulong> dependencies)
    {
        ulong revision = 0;
        foreach (KeyValuePair<SignalNode, ulong> dependency in dependencies)
        {
            revision = Math.Max(revision, dependency.Key.Revision);
        }

        return revision;
    }

    internal static void Evaluate(Action action, Dictionary<SignalNode, ulong> dependencies)
    {
        SignalContext context = Current;
        Dictionary<SignalNode, ulong>? previous = context.Reads;
        context.Reads = dependencies;
        context.Evaluating++;
        try
        {
            action();
        }
        finally
        {
            context.Evaluating--;
            context.Reads = previous;
        }
    }

    internal static T Evaluate<T>(Func<T> read, Dictionary<SignalNode, ulong> dependencies)
    {
        SignalContext context = Current;
        Dictionary<SignalNode, ulong>? previous = context.Reads;
        context.Reads = dependencies;
        context.Evaluating++;
        try
        {
            return read();
        }
        finally
        {
            context.Evaluating--;
            context.Reads = previous;
        }
    }

    internal void Flush()
    {
        if (Batching != 0 || Evaluating != 0 || Flushing)
        {
            return;
        }

        Flushing = true;
        List<Exception>? errors = null;
        try
        {
            while (true)
            {
                (SignalEffect Effect, ulong Generation) next;
                lock (Gate)
                {
                    if (!Pending.TryDequeue(out next))
                    {
                        break;
                    }
                }

                try
                {
                    next.Effect.Deliver(next.Generation);
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }
        }
        finally
        {
            Flushing = false;
        }

        if (errors is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }

        if (errors is not null)
        {
            throw new AggregateException(errors);
        }
    }
}
