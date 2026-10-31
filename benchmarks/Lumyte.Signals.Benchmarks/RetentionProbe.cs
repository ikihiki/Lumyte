using System.Globalization;
using System.Runtime.CompilerServices;

namespace Lumyte.Signals.Benchmarks;

internal static class RetentionProbe
{
    private const int ObserverCount = 1000;
    private const int DependencyCount = 16;
    private static Graph? _root;

    internal static void Run()
    {
        // Initialize callbacks, JIT paths, and small pool/queue caches before the baseline.
        _ = Install(32);
        Release();
        long before = Collect();

        WeakReference<object> payload = Install(ObserverCount);
        long active = Collect();
        Release();
        long released = Collect();
        bool payloadRetained = payload.TryGetTarget(out _);
        if (payloadRetained)
        {
            throw new InvalidOperationException("Disposed graphs must not retain their source payload through a pool.");
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"observers\":{ObserverCount},\"dependencies\":{DependencyCount},\"before_bytes\":{before},\"active_bytes\":{active},\"after_dispose_bytes\":{released},\"active_delta_bytes\":{active - before},\"after_dispose_delta_bytes\":{released - before},\"payload_retained\":{payloadRetained.ToString().ToLowerInvariant()}}}"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<object> Install(int observers)
    {
        _root = new Graph(observers);
        _root.Update();
        _root.Update();
        return _root.PayloadReference();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Release()
    {
        _root!.Dispose();
        _root = null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Collect()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private sealed class Graph : IDisposable
    {
        private readonly Signal<object>[] _inputs = new Signal<object>[DependencyCount];
        private readonly SignalEffect[] _effects;
        private int _notifications;

        internal Graph(int observers)
        {
            for (int index = 0; index < _inputs.Length; index++)
            {
                _inputs[index] = Signal.Create(new object());
            }

            _effects = new SignalEffect[observers];
            Action observe = ReadAll;
            for (int index = 0; index < _effects.Length; index++)
            {
                _effects[index] = Signal.Effect(observe);
            }
        }

        public void Dispose()
        {
            foreach (SignalEffect effect in _effects)
            {
                effect.Dispose();
            }

            int notifications = _notifications;
            _inputs[0].Value = new object();
            if (_notifications != notifications)
            {
                throw new InvalidOperationException("Disposed observers must not receive updates.");
            }

            Array.Clear(_effects);
            Array.Clear(_inputs);
        }

        internal void Update() => _inputs[0].Value = new object();

        internal WeakReference<object> PayloadReference() => new(_inputs[1].Peek());

        private void ReadAll()
        {
            foreach (Signal<object> input in _inputs)
            {
                _ = input.Value;
            }

            _notifications++;
        }
    }
}
