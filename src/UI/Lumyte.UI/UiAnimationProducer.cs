using Lumyte.Core.Time;
using Lumyte.Signals;

namespace Lumyte.UI;

internal abstract class UiAnimationProducer
{
    private readonly Dictionary<IUiAnimationProjection, int> _projections = [];
    private readonly Computed<bool> _evaluation;
    private readonly List<Action> _notifications = [];
    private SignalScope? _scope;
    private UiSurface? _surface;
    private int _references;
    private ulong _generation;
    private bool _clockInitialized;
    private TimePoint _lastWall;
    private bool _wasEnabled;

    protected UiAnimationProducer()
    {
        _evaluation = Signal.Computed(() =>
        {
            _ = EvaluationEpoch.Value;
            EvaluateCore();
            return true;
        });
    }

    internal Signal<ulong> EvaluationEpoch { get; } = Signal.Create(0UL);

    internal bool IsConnected => _surface is not null;

    internal ulong Generation => _generation;

    internal abstract ulong Revision { get; }

    internal abstract bool HasPending { get; }

    internal abstract bool IsRunning { get; }

    protected TimePoint ActiveTime { get; private set; }

    protected SignalScope Scope => _scope!;

    internal void Retain(UiSurface surface, IUiAnimationProjection projection)
    {
        if (_surface is not null && !ReferenceEquals(_surface, surface))
        {
            throw new InvalidOperationException("An animation producer cannot be connected to multiple surfaces.");
        }

        bool connected = _surface is null;
        if (connected)
        {
            _surface = surface;
            _scope = new();
            try
            {
                ConnectCore();
                EvaluationEpoch.Value = checked(EvaluationEpoch.Peek() + 1);
            }
            catch
            {
                Disconnect();
                throw;
            }
        }

        try
        {
            projection.Stage();
            projection.Publish();
            _projections[projection] = _projections.GetValueOrDefault(projection) + 1;
            _references++;
        }
        catch
        {
            if (connected)
            {
                Disconnect();
            }

            throw;
        }
    }

    internal void Release(IUiAnimationProjection projection)
    {
        int references = _projections[projection] - 1;
        if (references == 0)
        {
            _projections.Remove(projection);
        }
        else
        {
            _projections[projection] = references;
        }

        if (--_references == 0)
        {
            Disconnect();
        }
    }

    internal void Stage(TimePoint wall, bool enabled)
    {
        if (_clockInitialized && wall < _lastWall)
        {
            throw new InvalidOperationException("The animation clock moved backwards.");
        }

        if (_clockInitialized && enabled && _wasEnabled)
        {
            ActiveTime += wall - _lastWall;
        }

        _clockInitialized = true;
        _lastWall = wall;
        _wasEnabled = enabled;
        if (!enabled)
        {
            return;
        }

        _notifications.Clear();
        EvaluationEpoch.Value = checked(EvaluationEpoch.Peek() + 1);
        _ = _evaluation.Peek();
        foreach (IUiAnimationProjection projection in _projections.Keys)
        {
            projection.Stage();
        }
    }

    internal void Publish()
    {
        CommitCore();
        foreach (IUiAnimationProjection projection in _projections.Keys)
        {
            projection.Publish();
        }
    }

    internal Action[] TakeNotifications()
    {
        Action[] result = _notifications.ToArray();
        _notifications.Clear();
        return result;
    }

    internal void Freeze() => _wasEnabled = false;

    internal virtual bool Inherit(UiAnimationProducer previous) => false;

    protected abstract void ConnectCore();

    protected abstract void EvaluateCore();

    protected abstract void CommitCore();

    protected virtual void DisconnectCore()
    {
    }

    protected void Notify(Action? callback)
    {
        if (callback is not null)
        {
            _notifications.Add(callback);
        }
    }

    protected void RevokeNotifications() => _generation++;

    protected void InheritClock(UiAnimationProducer previous)
    {
        ActiveTime = previous.ActiveTime;
        _lastWall = previous._lastWall;
        _wasEnabled = previous._wasEnabled;
        _clockInitialized = previous._clockInitialized;
    }

    private void Disconnect()
    {
        DisconnectCore();
        _scope?.Dispose();
        _scope = null;
        _surface = null;
        _projections.Clear();
        _references = 0;
        _notifications.Clear();
        _generation++;
        _clockInitialized = false;
        _wasEnabled = false;
        ActiveTime = TimePoint.Zero;
    }
}
