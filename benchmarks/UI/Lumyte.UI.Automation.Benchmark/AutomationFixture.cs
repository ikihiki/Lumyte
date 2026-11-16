using Lumyte.Input.Abstractions;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Automation.Benchmark;

/// <summary>Single-execution-domain CPU host with explicit frame and input completion counters.</summary>
public sealed class AutomationFixture : IUiAutomationHost, IDisposable
{
    private readonly SemaphoreSlim _input = new(1, 1);
    private readonly Action? _advance;
    private readonly bool _ownsSurface = true;
    private readonly Clock _clock = new();
    private Lease? _active;
    private bool _disposed;
    private int _target;
    private long _pointer;

    /// <summary>Initializes a new instance of the <see cref="AutomationFixture"/> class.</summary>
    /// <param name="elements">Number of independent ordinary buttons.</param>
    public AutomationFixture(int elements)
    {
        Definitions.Stack root = Stack(testId: "controls");
        root.Children.Initialize(Enumerable.Range(0, elements).Select(index => Button(testId: $"control-{index}", automationName: index % 2 == 0 ? "Primary" : "Secondary", width: 40, height: 10, onClick: () => Clicks++)).ToArray());
        Surface = new(root);
        Surface.SetViewport(new(100, elements * 10));
        Surface.UpdateLayout();
        Page = new(this);
    }

    /// <summary>Initializes a new instance of the <see cref="AutomationFixture"/> class borrowing a popup surface.</summary>
    /// <param name="surface">Borrowed registered surface.</param>
    /// <param name="input">Shared input exclusion domain.</param>
    /// <param name="advance">Actual manager frame advance.</param>
    public AutomationFixture(UiSurface surface, SemaphoreSlim input, Action advance)
    {
        Surface = surface;
        _input = input;
        _advance = advance;
        _ownsSurface = false;
        Page = new(this);
    }

    /// <summary>Gets the owned surface.</summary>
    public UiSurface Surface { get; }

    /// <summary>Gets the ordinary automation page.</summary>
    public UiAutomationPage Page { get; }

    /// <summary>Gets completed layout frames, not snapshot sequence numbers.</summary>
    public int FrameOrdinal { get; private set; }

    /// <summary>Gets dispatched input transitions.</summary>
    public int ProcessedInputs { get; private set; }

    /// <summary>Gets completed activation callbacks.</summary>
    public int Clicks { get; private set; }

    /// <inheritdoc/>
    public TimeProvider TimeProvider => _clock;

    /// <summary>Alternates between finite existing target identities.</summary>
    /// <returns>The next test ID.</returns>
    public string NextTarget() => $"control-{_target ^= 1}";

    /// <inheritdoc/>
    public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ValueTask.FromResult(action(Surface));
    }

    /// <inheritdoc/>
    public ValueTask NextFrameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _clock.Advance();
        _advance?.Invoke();
        Surface.UpdateLayout();
        FrameOrdinal++;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _input.WaitAsync(cancellationToken);
        _active = new(this, ++_pointer);
        return _active;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _active?.DisposeAsync().GetAwaiter().GetResult();
            _disposed = true;
            if (_ownsSurface)
            {
                Surface.Dispose();
                _input.Dispose();
            }
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        internal void Advance() => _ticks += TimeSpan.TicksPerMillisecond;
    }

    private sealed class Lease(AutomationFixture owner, long pointer) : IUiAutomationInputLease
    {
        private readonly List<Key> _keys = [];
        private bool _down;
        private bool _released;

        public long PointerId => pointer;

        public bool Dispatch(UiSurface target, UiInput input)
        {
            ObjectDisposedException.ThrowIf(_released || owner._disposed, this);
            if (!ReferenceEquals(target, owner.Surface))
            {
                throw new ArgumentException("Wrong surface.", nameof(target));
            }

            if (input is UiPointerInput p)
            {
                if (p.PointerId != pointer)
                {
                    throw new ArgumentException("Wrong pointer.", nameof(input));
                }

                _down = p.Phase == UiPointerPhase.Down || (_down && p.Phase == UiPointerPhase.Move);
            }
            else if (input is UiKeyInput key)
            {
                if (key.IsDown && !_keys.Contains(key.Key))
                {
                    _keys.Add(key.Key);
                }
                else if (!key.IsDown)
                {
                    _keys.Remove(key.Key);
                }
            }

            owner.ProcessedInputs++;
            return target.Dispatch(input);
        }

        public ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;
                try
                {
                    if (_down)
                    {
                        owner.Surface.Dispatch(new UiPointerInput(pointer, UiPointerPhase.Cancel, default));
                    }

                    if (!owner._ownsSurface)
                    {
                        // Popup routing bypasses inner Dispatch; this exclusive CPU domain owns all pending surface input.
                        owner.Surface.Dispatch(new UiFocusLostInput());
                    }

                    foreach (Key key in _keys)
                    {
                        owner.Surface.Dispatch(new UiKeyCancelInput(key));
                        owner.Surface.Dispatch(new UiKeyInput(key, false));
                    }
                }
                finally
                {
                    owner._active = null;
                    owner._input.Release();
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
