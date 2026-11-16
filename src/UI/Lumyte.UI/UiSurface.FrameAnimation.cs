using Lumyte.Core.Time;

namespace Lumyte.UI;

/// <summary>Connects custom visual animations to the borrowed surface clock.</summary>
public sealed partial class UiSurface
{
    private readonly HashSet<UiElement> _frameAnimations = [];
    private bool _notifyingFrameFreeze;

    internal void AddFrameAnimation(UiElement element) => _frameAnimations.Add(element);

    internal void RemoveFrameAnimation(UiElement element) => _frameAnimations.Remove(element);

    internal void FreezeHiddenFrameAnimations() => FreezeFrameAnimations(hiddenOnly: true);

    private void FreezeFrameAnimations(bool hiddenOnly = false)
    {
        // Callbacks may rebuild or dispose: snapshot membership and reject recursion.
        if (_notifyingFrameFreeze)
        {
            return;
        }

        _notifyingFrameFreeze = true;
        Exception? failure = null;
        UiAnimationProducer[] producers = _animationProducers.ToArray();
        UiElement[] participants = _frameAnimations.ToArray();
        var notified = new HashSet<UiElement>();
        try
        {
            foreach (UiElement element in participants)
            {
                if (ReferenceEquals(element.Surface, this) && (!hiddenOnly || !IsAnimationVisible(element)))
                {
                    try
                    {
                        notified.Add(element);
                        element.AdvanceFrameAnimation(null);
                    }
                    catch (Exception error)
                    {
                        failure ??= error;
                        LatchAnimationFailure(error);
                    }
                }
            }

            // A hidden-hook failure stops the whole animation phase, including visible peers.
            if (failure is not null && hiddenOnly)
            {
                foreach (UiElement element in participants)
                {
                    if (!notified.Contains(element) && ReferenceEquals(element.Surface, this))
                    {
                        try
                        {
                            element.AdvanceFrameAnimation(null);
                        }
                        catch (Exception error)
                        {
                            LatchAnimationFailure(error);
                        }
                    }
                }
            }

            // A failed notification must not leave any old value producer running.
            if (!hiddenOnly || failure is not null)
            {
                foreach (UiAnimationProducer producer in producers)
                {
                    producer.Freeze();
                }
            }
        }
        finally
        {
            _notifyingFrameFreeze = false;
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void LatchAnimationFailure(Exception error)
    {
        _animationFailure ??= error;
        _signalFailure = new UiStateUpdateException(_animationFailure);
        _animationUpdatesEnabled = false;
    }

    private bool HasVisibleFrameAnimation()
    {
        foreach (UiElement element in _frameAnimations)
        {
            if (IsAnimationVisible(element))
            {
                return true;
            }
        }

        return false;
    }

    private void AdvanceFrameAnimations(TimePoint? now)
    {
        FreezeHiddenFrameAnimations();
        foreach (UiElement element in _frameAnimations.ToArray())
        {
            if (_isDisposed || !_animationUpdatesEnabled || _animationFailure is not null)
            {
                break;
            }

            if (ReferenceEquals(element.Surface, this) && IsAnimationVisible(element))
            {
                element.AdvanceFrameAnimation(now);
            }
        }
    }
}
