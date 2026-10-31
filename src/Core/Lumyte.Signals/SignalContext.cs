using System.Runtime.ExceptionServices;

namespace Lumyte.Signals;

internal sealed class SignalContext
{
    [ThreadStatic]
    private static SignalContext? _current;
    private static ulong _revision;

    internal static object Gate { get; } = new();

    internal static SignalContext Current => _current ??= new();

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

    internal static T Evaluate<T>(Func<T> read, out Dictionary<SignalNode, ulong> dependencies)
    {
        SignalContext context = Current;
        Dictionary<SignalNode, ulong>? previous = context.Reads;
        dependencies = [];
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
