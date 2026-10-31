using System.Runtime.ExceptionServices;

namespace Lumyte.Signals;

/// <summary>Creates independent reactive values and automatically delivered observers.</summary>
public static partial class Signal
{
    /// <summary>Creates a mutable signal; values are compared rather than copied.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="initialValue">The initial value.</param>
    /// <param name="comparer">A pure equality comparer, or the default comparer.</param>
    /// <returns>The independent signal.</returns>
    public static Signal<T> Create<T>(T initialValue, IEqualityComparer<T>? comparer = null) => new(initialValue, comparer ?? EqualityComparer<T>.Default);

    /// <summary>Creates a lazy synchronous derived value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="compute">The pure synchronous computation.</param>
    /// <param name="comparer">The pure result comparer.</param>
    /// <returns>A lazy derived signal.</returns>
    public static Computed<T> Computed<T>(Func<T> compute, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(compute);
        return new(compute, comparer ?? EqualityComparer<T>.Default);
    }

    /// <summary>Runs once synchronously, then reacts automatically until disposed.</summary>
    /// <param name="effect">A short synchronous observer.</param>
    /// <param name="scheduler">An optional host delivery boundary.</param>
    /// <param name="onError">Observes subsequent failures once.</param>
    /// <returns>The owned observer handle.</returns>
    public static SignalEffect Effect(Action effect, ISignalScheduler? scheduler = null, Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        lock (SignalContext.Gate)
        {
            SignalContext.CheckMutation();
            var result = new SignalEffect(effect, scheduler, onError);
            result.Initialize();
            SignalContext.Current.Scope?.Add(result);
            return result;
        }
    }

    /// <summary>Coalesces observer delivery while preserving completed assignments, including on failure.</summary>
    /// <param name="update">The synchronous mutation block.</param>
    public static void Batch(Action update)
    {
        ArgumentNullException.ThrowIfNull(update);
        SignalContext context = SignalContext.Current;
        lock (SignalContext.Gate)
        {
            SignalContext.CheckMutation();
            context.Batching++;
        }

        Exception? failure = null;
        try
        {
            update();
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            lock (SignalContext.Gate)
            {
                context.Batching--;
            }
        }

        try
        {
            context.Flush();
        }
        catch (Exception error)
        {
            if (failure is not null)
            {
                throw new AggregateException(failure, error);
            }

            throw;
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>Reads without adding dependencies to the surrounding evaluation.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="read">The synchronous reader.</param>
    /// <returns>The reader's value.</returns>
    public static T Untracked<T>(Func<T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        lock (SignalContext.Gate)
        {
            SignalContext context = SignalContext.Current;
            Dictionary<SignalNode, ulong>? previous = context.Reads;
            context.Reads = null;
            try
            {
                return read();
            }
            finally
            {
                context.Reads = previous;
            }
        }
    }

    /// <summary>Captures passive read dependencies for hosts that rebuild outside an Effect evaluation.</summary>
    /// <typeparam name="T">The construction result type.</typeparam>
    /// <param name="build">Constructs a value while recording read dependencies.</param>
    /// <param name="reads">The passive read set, with no subscriptions.</param>
    /// <returns>The constructed value.</returns>
    public static T CaptureReads<T>(Func<T> build, out SignalReadSet reads)
    {
        ArgumentNullException.ThrowIfNull(build);
        lock (SignalContext.Gate)
        {
            SignalContext context = SignalContext.Current;
            Dictionary<SignalNode, ulong>? previous = context.Reads;
            var dependencies = new Dictionary<SignalNode, ulong>();
            context.Reads = dependencies;
            try
            {
                return build();
            }
            finally
            {
                context.Reads = previous;
                reads = new(dependencies);
                if (previous is not null)
                {
                    foreach ((SignalNode node, ulong revision) in dependencies)
                    {
                        previous.TryAdd(node, revision);
                    }
                }
            }
        }
    }
}
