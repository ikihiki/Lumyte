using System.Runtime.ExceptionServices;
using Lumyte.Input.Abstractions;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly Dictionary<Key, Widget> _keyCaptures = new();
    private readonly HashSet<Key> _consumedKeys = new();
    private readonly List<Widget> _focusCandidates = new();
    private Widget? _focusedWidget;
    private long _focusChange;
    private long _focusRequest;
    private long _inputReset;

    /// <summary>Gets the surface-local focused widget, or null; the host owns operating-system focus.</summary>
    public Widget? FocusedWidget
    {
        get
        {
            ThrowIfDisposed();
            return _focusedWidget;
        }
    }

    internal bool IsFocused(Widget target) => ReferenceEquals(_focusedWidget, target);

    internal bool RequestFocus(Widget target)
    {
        if (_isDisposed || !IsFocusEligible(target))
        {
            return false;
        }

        _focusRequest++;
        return ChangeFocus(target);
    }

    internal bool CaptureKey(Widget target, UiInput input)
    {
        ThrowIfDisposed();
        if (input is not UiKeyInput { IsDown: true } key)
        {
            throw new InvalidOperationException("Key capture requires key Down.");
        }

        if (!ReferenceEquals(target.Surface, this) || !ReferenceEquals(target.InputVersion, _inputVersion))
        {
            return false;
        }

        if (_keyCaptures.TryGetValue(key.Key, out Widget? owner))
        {
            return ReferenceEquals(owner, target);
        }

        if (key.IsRepeat || _consumedKeys.Contains(key.Key))
        {
            return false;
        }

        _keyCaptures.Add(key.Key, target);
        _consumedKeys.Add(key.Key);
        return true;
    }

    internal bool CancelKey(Key key, Widget? expected = null)
    {
        if (!_keyCaptures.TryGetValue(key, out Widget? owner)
            || (expected is not null && !ReferenceEquals(owner, expected)))
        {
            return false;
        }

        _keyCaptures.Remove(key);
        DeliverInput(owner, new UiKeyCancelInput(key), false, true);
        return true;
    }

    private static void ThrowInputFailures(List<Exception>? failures)
    {
        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is not null)
        {
            throw new AggregateException(failures);
        }
    }

    private bool IsFocusEligible(Widget target) =>
        ReferenceEquals(target.Surface, this) && ReferenceEquals(target.InputVersion, _inputVersion) && target.InputFocusable;

    private bool ChangeFocus(Widget? target)
    {
        if (target is not null && !IsFocusEligible(target))
        {
            return false;
        }

        if (ReferenceEquals(_focusedWidget, target))
        {
            return true;
        }

        Widget? previous = _focusedWidget;
        _focusedWidget = target;
        long revision = ++_focusChange;
        KeyValuePair<Key, Widget>[] captures = _keyCaptures.ToArray();
        _keyCaptures.Clear();
        List<Exception>? failures = null;
        CancelDetachedKeys(captures, ref failures);
        if (previous is not null && !ReferenceEquals(_focusedWidget, previous))
        {
            NotifyFocus(previous, false, ref failures);
        }

        if (!_isDisposed && _focusChange == revision && target is not null && IsFocusEligible(target))
        {
            NotifyFocus(target, true, ref failures);
        }

        ThrowInputFailures(failures);
        return true;
    }

    private void NotifyFocus(Widget target, bool isFocused, ref List<Exception>? failures)
    {
        try
        {
            DeliverInput(target, new UiFocusChangedInput(isFocused), false);
        }
        catch (Exception failure)
        {
            (failures ??= new()).Add(failure);
        }
    }

    private void CancelDetachedKeys(KeyValuePair<Key, Widget>[] captures, ref List<Exception>? failures)
    {
        foreach ((Key key, Widget target) in captures)
        {
            try
            {
                DeliverInput(target, new UiKeyCancelInput(key), false, true);
            }
            catch (Exception failure)
            {
                (failures ??= new()).Add(failure);
            }
        }
    }

    private bool DispatchKey(UiKeyInput key)
    {
        if (key.IsRepeat && !key.IsDown)
        {
            throw new ArgumentException("Only key Down can repeat.", nameof(key));
        }

        if ((key.Modifiers & ~(UiModifiers.Shift | UiModifiers.Control | UiModifiers.Alt | UiModifiers.Meta)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }

        if (key.Key == Key.Unknown)
        {
            return false;
        }

        if (_keyCaptures.TryGetValue(key.Key, out Widget? owner))
        {
            if (!key.IsDown)
            {
                _keyCaptures.Remove(key.Key);
                _consumedKeys.Remove(key.Key);
            }
            else if (!key.IsRepeat)
            {
                return true;
            }

            DeliverInput(owner, key, false, true);
            return true;
        }

        if (_consumedKeys.Contains(key.Key))
        {
            if (!key.IsDown)
            {
                _consumedKeys.Remove(key.Key);
            }

            return true;
        }

        if (key.IsRepeat)
        {
            return false;
        }

        long reset = _inputReset;
        UiInputResult result = RouteInputResult(_focusedWidget ?? _root, key);
        if (result == UiInputResult.Handled)
        {
            if (key.IsDown && !_isDisposed && _inputReset == reset)
            {
                _consumedKeys.Add(key.Key);
            }

            return true;
        }

        if (_isDisposed || _inputReset != reset || result == UiInputResult.StopPropagation || !key.IsDown
            || key.Key != Key.Tab || (key.Modifiers & ~UiModifiers.Shift) != 0 || _focusCandidates.Count == 0)
        {
            return false;
        }

        int current = -1;
        for (int index = 0; index < _focusCandidates.Count; index++)
        {
            if (ReferenceEquals(_focusCandidates[index], _focusedWidget))
            {
                current = index;
                break;
            }
        }

        int next = (key.Modifiers & UiModifiers.Shift) != 0
            ? (current <= 0 ? _focusCandidates.Count - 1 : current - 1)
            : (current + 1) % _focusCandidates.Count;
        Widget candidate = _focusCandidates[next];
        _consumedKeys.Add(key.Key);
        return ChangeFocus(candidate);
    }

    private void RebuildFocusCandidates()
    {
        _focusCandidates.Clear();
        _root.CollectFocusCandidates(_focusCandidates, _inputVersion!);
    }

    private void RefreshFocusState()
    {
        if (_focusedWidget is not null && !IsFocusEligible(_focusedWidget))
        {
            ChangeFocus(null);
        }

        foreach ((Key key, Widget target) in _keyCaptures.ToArray())
        {
            if (_isDisposed)
            {
                break;
            }

            if (!ReferenceEquals(target.InputVersion, _inputVersion)
                && _keyCaptures.TryGetValue(key, out Widget? current) && ReferenceEquals(current, target))
            {
                CancelKey(key, target);
            }
        }
    }

    private bool CancelAllInput()
    {
        KeyValuePair<long, PointerCapture>[] pointers = _pointerCaptures.ToArray();
        KeyValuePair<Key, Widget>[] keys = _keyCaptures.ToArray();
        Widget? focused = _focusedWidget;
        bool consumed = pointers.Length != 0 || keys.Length != 0 || _consumedKeys.Count != 0 || focused is not null;
        _pointerCaptures.Clear();
        _keyCaptures.Clear();
        _consumedKeys.Clear();
        _focusedWidget = null;
        _focusChange++;
        _inputReset++;
        List<Exception>? failures = null;
        foreach ((long pointerId, PointerCapture capture) in pointers)
        {
            try
            {
                NotifyReleased(pointerId, capture, true);
            }
            catch (Exception failure)
            {
                (failures ??= new()).Add(failure);
            }
        }

        CancelDetachedKeys(keys, ref failures);
        if (focused is not null)
        {
            NotifyFocus(focused, false, ref failures);
        }

        ThrowInputFailures(failures);
        return consumed;
    }
}
