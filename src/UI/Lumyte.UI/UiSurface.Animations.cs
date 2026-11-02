using System.Runtime.ExceptionServices;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly HashSet<UiAnimationProducer> _animationProducers = [];
    private readonly HashSet<UiAnimationProducer> _visibleAnimationProducers = [];
    private readonly Dictionary<UiElement, AnimationLease> _animatedElements = new(ReferenceEqualityComparer.Instance);
    private IMonotonicClock _animationClock = new SystemMonotonicClock();
    private bool _animationUpdatesEnabled = true;
    private bool _advancingAnimations;
    private bool _publishingAnimations;
    private Exception? _animationFailure;

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="root">The disconnected declaration tree.</param>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    /// <param name="animationClock">The borrowed monotonic animation clock.</param>
    public UiSurface(Widget root, IUiDispatcher dispatcher, IMonotonicClock animationClock)
        : this(RequireAnimationClock(root, animationClock), dispatcher)
    {
        _animationClock = animationClock;
    }

    /// <summary>Gets a value indicating whether any visible producer needs continuous time progression.</summary>
    public bool HasRunningAnimations => !_isDisposed && _animationUpdatesEnabled && _animationFailure is null && (HasVisibleAnimation(true) || _presenceEntries.Any(entry => entry.IsActive));

    /// <summary>Gets a value indicating whether visible animations have a one-shot target or control update.</summary>
    public bool HasPendingAnimationUpdates => !_isDisposed && _animationUpdatesEnabled && _animationFailure is null && (HasVisibleAnimation(false) || _presenceEntries.Any(entry => entry.WantsCapture || entry.HasNotification));

    /// <summary>Freezes or resumes animation time without stopping independent model values.</summary>
    /// <param name="enabled">Whether the owning host can update and present this surface.</param>
    public void SetAnimationUpdatesEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_animationUpdatesEnabled == enabled)
        {
            return;
        }

        _animationUpdatesEnabled = enabled;
        if (!enabled)
        {
            InterruptVisuals();
        }

        foreach (UiAnimationProducer producer in _animationProducers)
        {
            producer.Freeze();
        }
    }

    /// <summary>Evaluates one animation phase; layout, recording and getters never advance time.</summary>
    public void AdvanceAnimations()
    {
        ThrowIfDisposed();
        if (_animationFailure is { } failed)
        {
            ExceptionDispatchInfo.Capture(failed).Throw();
        }

        if (_advancingAnimations || _dispatchDepth != 0 || _layoutPass is not null || _recordDepth != 0 || _applyingRebuild)
        {
            throw new InvalidOperationException("Advance animations once at the owner's update boundary.");
        }

        if (!_animationUpdatesEnabled)
        {
            return;
        }

        _advancingAnimations = true;
        try
        {
            UpdateTree();
            if (_presenceEntries.Count != 0)
            {
                AdvanceVisuals(_animationClock.Now);
            }

            if (_isDisposed || _animationProducers.Count == 0)
            {
                return;
            }

            if (!HasAnimationWork())
            {
                return;
            }

            HashSet<UiAnimationProducer> visible = _visibleAnimationProducers;
            visible.Clear();
            foreach (KeyValuePair<UiElement, AnimationLease> entry in _animatedElements)
            {
                AnimationLease lease = entry.Value;
                for (int index = 0; index < lease.Projections.Count; index++)
                {
                    UiAnimationProducer producer = lease.Projections[index].Producer;
                    if ((producer.IsRunning || producer.HasPending) && !visible.Contains(producer) && IsAnimationVisible(entry.Key))
                    {
                        visible.Add(producer);
                    }
                }
            }

            bool needsEvaluation = false;
            foreach (UiAnimationProducer producer in _animationProducers)
            {
                if (!visible.Contains(producer))
                {
                    producer.Freeze();
                }
                else
                {
                    needsEvaluation |= producer.IsRunning || producer.HasPending;
                }
            }

            if (!needsEvaluation)
            {
                return;
            }

            TimePoint now = _animationClock.Now;
            UiAnimationProducer[] snapshot = _animationProducers.ToArray();
            var callbacks = new List<(UiAnimationProducer Producer, ulong Generation, Action Callback)>();
            _publishingAnimations = true;
            try
            {
                foreach (UiAnimationProducer producer in snapshot)
                {
                    producer.Stage(now, visible.Contains(producer));
                }

                PublishAnimationValues(snapshot, visible);

                foreach (UiAnimationProducer producer in snapshot)
                {
                    foreach (Action callback in producer.TakeNotifications())
                    {
                        callbacks.Add((producer, producer.Generation, callback));
                    }
                }
            }
            finally
            {
                _publishingAnimations = false;
            }

            UpdateTree();
            foreach ((UiAnimationProducer producer, ulong generation, Action callback) in callbacks)
            {
                if (_isDisposed || !_animationUpdatesEnabled)
                {
                    break;
                }

                if (producer.IsConnected && _animationProducers.Contains(producer) && producer.Generation == generation)
                {
                    callback();
                    if (!_isDisposed)
                    {
                        UpdateTree();
                    }
                }
            }
        }
        catch (Exception error)
        {
            _animationFailure = error;
            _signalFailure = new UiStateUpdateException(error);
            _animationUpdatesEnabled = false;
            throw;
        }
        finally
        {
            _advancingAnimations = false;
        }
    }

    internal void SetAnimationClock(IMonotonicClock clock) => _animationClock = clock;

    private static Widget RequireAnimationClock(Widget root, IMonotonicClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        return root;
    }

    private static void PublishAnimationValues(UiAnimationProducer[] snapshot, HashSet<UiAnimationProducer> visible)
    {
        Signal.Batch(() =>
        {
            foreach (UiAnimationProducer producer in snapshot)
            {
                if (visible.Contains(producer))
                {
                    producer.Publish();
                }
            }
        });
    }

    private static bool IsAnimationVisible(UiElement element)
    {
        for (UiElement? current = element; current is not null; current = current.Parent)
        {
            if (!current.LayoutVisible)
            {
                return false;
            }
        }

        return true;
    }

    private bool HasAnimationWork()
    {
        foreach (UiAnimationProducer producer in _animationProducers)
        {
            if (producer.IsRunning || producer.HasPending)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasVisibleAnimation(bool running)
    {
        bool hasWork = false;
        foreach (UiAnimationProducer producer in _animationProducers)
        {
            if (running ? producer.IsRunning : producer.HasPending)
            {
                hasWork = true;
                break;
            }
        }

        if (!hasWork)
        {
            return false;
        }

        foreach (KeyValuePair<UiElement, AnimationLease> entry in _animatedElements)
        {
            if (!IsAnimationVisible(entry.Key))
            {
                continue;
            }

            AnimationLease lease = entry.Value;
            for (int index = 0; index < lease.Projections.Count; index++)
            {
                UiAnimationProducer producer = lease.Projections[index].Producer;
                if (running ? producer.IsRunning : producer.HasPending)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
