using System.Numerics;
using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Application;

internal sealed class UiHostInputRouter
{
    private readonly IWindowInputSource _map;
    private readonly Func<InputWindowId, InputDeviceId?, UiInput, bool>? _dispatch;
    private readonly Dictionary<UiSurface, Target> _targets = [];
    private readonly Dictionary<InputWindowId, Target> _keyboardTargets = [];

    private readonly Dictionary<(InputDeviceId Device, int Kind, ulong Contact), Pointer> _pointers = [];

    private readonly Dictionary<(InputDeviceId Device, Key Key), KeyOwner> _keys = [];

    private readonly Dictionary<(InputDeviceId Device, InputWindowId Origin), (Vector2 Position, InputCaptureStamp? Capture, ulong Sequence)> _positions = [];

    private readonly Dictionary<InputWindowId, HashSet<(InputDeviceId Device, Key Key)>> _modifiers = [];
    private readonly Dictionary<InputDeviceId, HashSet<MouseButton>> _mouseButtons = [];
    private ulong _sequence;
    private InputDeviceId? _currentDevice;

    internal UiHostInputRouter(IOrderedInputSource source, IWindowInputSource windowMap, Func<InputWindowId, InputDeviceId?, UiInput, bool>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(windowMap);
        if (source.StreamId == Guid.Empty || source.StreamId != windowMap.StreamId)
        {
            throw new ArgumentException("The ordered source and presentation map must share a stream.");
        }

        Source = source;
        _map = windowMap;
        _dispatch = dispatch;
    }

    internal IOrderedInputSource Source { get; }

    internal void Attach(IWindow window, UiSurface surface) => AttachCore(window, null, surface);

    internal void Attach(WindowNode node, UiSurface surface) => AttachCore(node.PresentationRoot.NativeWindow, node, surface);

    internal void Detach(UiSurface surface)
    {
        if (_targets.Remove(surface, out Target? target))
        {
            CancelTarget(target);
        }
    }

    internal void Reset(InputWindowId origin)
    {
        _positions.Keys.Where(key => key.Origin == origin).ToList().ForEach(key => _positions.Remove(key));
        _modifiers.Remove(origin);
        Target[] targets = _targets.Values.Where(target => target.Origin == origin).ToArray();
        var errors = new List<Exception>();
        foreach (Target target in targets)
        {
            try
            {
                CancelTarget(target);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        if (errors.Count != 0)
        {
            throw new AggregateException(errors);
        }
    }

    internal bool Dispatch(InputRecord input)
    {
        if (input.Sequence == _sequence)
        {
            return false;
        }

        if (input.Sequence < _sequence)
        {
            throw new InvalidOperationException("UI input records must retain their system order.");
        }

        _sequence = input.Sequence;
        _currentDevice = input.DeviceId;
        if (input.Data is DeviceDisconnectedData)
        {
            return Disconnect(input.DeviceId);
        }

        if (input.Capture is InputCaptureStamp cause && cause.StreamId != Source.StreamId)
        {
            throw new ArgumentException("The record belongs to another acquisition stream.", nameof(input));
        }

        if (input.Data is FocusData { IsFocused: false } or WindowInputClosedData)
        {
            if (input.StateWindowId is InputWindowId scope)
            {
                Reset(scope);
            }

            return false;
        }

        if (input.Data is KeyCanceledData keyCanceled)
        {
            (InputDeviceId DeviceId, Key Key) identity = (input.DeviceId, keyCanceled.Key);
            foreach (HashSet<(InputDeviceId Device, Key Key)> modifiers in _modifiers.Values)
            {
                modifiers.Remove(identity);
            }

            if (_keys.Remove(identity, out KeyOwner? owner) && owner.Target is Target target)
            {
                Deliver(target, new UiKeyCancelInput(keyCanceled.Key));
                return true;
            }

            return false;
        }

        InputWindowId? origin = input.WindowId;
        if (input.Data is MouseMoveData move && origin is InputWindowId positionOrigin)
        {
            _positions[(input.DeviceId, positionOrigin)] = (move.Position, input.Capture, input.Sequence);
            return PointerEvent(input, 0, 0, UiPointerPhase.Move, move.Position);
        }

        if (input.Data is MouseButtonData mouse)
        {
            HashSet<MouseButton> buttons = _mouseButtons.GetValueOrDefault(input.DeviceId) ?? [];
            _mouseButtons[input.DeviceId] = buttons;
            if (mouse.IsDown)
            {
                buttons.Add(mouse.Button);
            }
            else
            {
                buttons.Remove(mouse.Button);
            }

            if (mouse.Button != MouseButton.Left)
            {
                bool consumed = _pointers.TryGetValue((input.DeviceId, 0, 0), out Pointer? series) && series.Consumed;
                if (buttons.Count == 0 && series is not null)
                {
                    series.Consumed = false;
                    series.Target = null;
                }

                return consumed;
            }

            if (mouse.IsDown && buttons.Count > 1 && (!_pointers.TryGetValue((input.DeviceId, 0, 0), out Pointer? previous) || !previous.Consumed))
            {
                // A mouse series already passed to the game cannot be acquired halfway through.
                return false;
            }

            bool positionKnown = origin is InputWindowId id && _positions.TryGetValue((input.DeviceId, id), out (Vector2 Position, InputCaptureStamp? Capture, ulong Sequence) position) && position.Capture == input.Capture && input.Capture is not null && input.Sequence - position.Sequence == 1;
            Vector2 point = positionKnown ? _positions[(input.DeviceId, origin!.Value)].Position : default;
            return PointerEvent(input, 0, 0, !positionKnown ? UiPointerPhase.Cancel : mouse.IsDown ? UiPointerPhase.Down : UiPointerPhase.Up, point, buttons.Count != 0);
        }

        if (input.Data is MouseButtonCanceledData canceled)
        {
            HashSet<MouseButton> buttons = _mouseButtons.GetValueOrDefault(input.DeviceId) ?? [];
            buttons.Remove(canceled.Button);
            if (canceled.Button == MouseButton.Left)
            {
                return PointerEvent(input, 0, 0, UiPointerPhase.Cancel, default, buttons.Count != 0);
            }

            bool consumed = _pointers.TryGetValue((input.DeviceId, 0, 0), out Pointer? series) && series.Consumed;
            if (buttons.Count == 0 && series is not null)
            {
                series.Consumed = false;
                series.Target = null;
            }

            return consumed;
        }

        if (input.Data is TouchData touch)
        {
            UiPointerPhase phase = touch.Phase switch
            {
                TouchPhase.Began => UiPointerPhase.Down,
                TouchPhase.Moved => UiPointerPhase.Move,
                TouchPhase.Ended => UiPointerPhase.Up,
                _ => UiPointerPhase.Cancel,
            };
            return PointerEvent(input, 1, touch.ContactId.Value, phase, touch.Position);
        }

        if (input.Data is PenData pen && (pen.IsInContact || pen.Phase is PenPhase.Up or PenPhase.Canceled or PenPhase.Left))
        {
            UiPointerPhase phase = pen.Phase switch
            {
                PenPhase.Down => UiPointerPhase.Down,
                PenPhase.Moved => UiPointerPhase.Move,
                PenPhase.Up => UiPointerPhase.Up,
                _ => UiPointerPhase.Cancel,
            };
            return PointerEvent(input, 2, pen.PointerId.Value, phase, pen.Position);
        }

        return input.Data is KeyData key && key.Key != Key.Unknown && KeyEvent(input, key);
    }

    private static (Vector2 Local, bool Inside) Local(Target target, Vector2 point)
    {
        if (target.Node is null)
        {
            return (point, point.X >= 0 && point.Y >= 0 && point.X < target.Window.State.ClientSize.X && point.Y < target.Window.State.ClientSize.Y);
        }

        (Vector2 origin, Lumyte.Graphics.Drawing2D.Rect2D clip) = UiHostDrawing.Geometry(target.Node);
        return (point - origin, point.X >= clip.X && point.Y >= clip.Y && point.X < clip.X + clip.Width && point.Y < clip.Y + clip.Height);
    }

    private bool Live(Target target) => UiHostDrawing.Drawable(target.Window.State) && _map.TryGetInputWindowId(target.Window.Id, out InputWindowId id) && id == target.Origin && (target.Node is null || (target.Node.IsEffectivelyVisible && target.Node.Lifetime == WindowLifetime.Open));

    private void AttachCore(IWindow window, WindowNode? node, UiSurface surface)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(surface);
        if (!_map.TryGetInputWindowId(window.Id, out InputWindowId origin) || _targets.ContainsKey(surface) || _targets.Values.Any(target => ReferenceEquals(target.Window, window) && ReferenceEquals(target.Node, node)))
        {
            throw new ArgumentException("The window must be live and each presentation target/surface registered only once.");
        }

        if (_targets.Values.Any(target => target.Origin == origin && (target.Node is null || node is null || target.Node.PresentationRoot != node.PresentationRoot)))
        {
            throw new ArgumentException("An origin cannot belong to different presentation roots.");
        }

        _targets.Add(surface, new(window, node, surface, origin));
    }

    private bool Deliver(Target target, UiInput input) => _dispatch is null ? target.Surface.Dispatch(input) : _dispatch(target.Origin, _currentDevice, input);

    private bool PointerEvent(InputRecord input, int kind, ulong contact, UiPointerPhase phase, Vector2 position, bool keepMouseSeries = false)
    {
        (InputDeviceId Device, int Kind, ulong Contact) identity = (input.DeviceId, kind, contact);
        if (!_pointers.TryGetValue(identity, out Pointer? pointer))
        {
            pointer = new(UiApplicationAutomationHost.NextPointerIdentity());
            _pointers.Add(identity, pointer);
        }

        bool terminal = phase is UiPointerPhase.Up or UiPointerPhase.Cancel;
        if (pointer.Consumed)
        {
            Target? owner = pointer.Target;
            if (terminal && !keepMouseSeries)
            {
                pointer.Target = null;
                pointer.Consumed = false;
            }

            try
            {
                if (owner is not null)
                {
                    if (!Live(owner) || input.WindowId != owner.Origin)
                    {
                        pointer.Target = null;
                        Deliver(owner, new UiPointerInput(pointer.Id, UiPointerPhase.Cancel, default));
                    }
                    else
                    {
                        (Vector2 local, bool visible) = Local(owner, position);
                        UiPointerPhase delivered = phase == UiPointerPhase.Up && !visible ? UiPointerPhase.Cancel : phase;
                        Deliver(owner, new UiPointerInput(pointer.Id, delivered, visible ? local : new(-1, -1)));
                    }
                }
            }
            catch
            {
                pointer.Target = null;
                throw;
            }
            finally
            {
                if (terminal && kind != 0)
                {
                    _pointers.Remove(identity);
                }
            }

            return true;
        }

        if (phase is not (UiPointerPhase.Down or UiPointerPhase.Move) || input.WindowId is not InputWindowId origin)
        {
            if (terminal && kind != 0)
            {
                _pointers.Remove(identity);
            }

            return false;
        }

        Target? selected = Select(origin, position);
        if (selected is null)
        {
            return false;
        }

        (Vector2 point, bool inside) = Local(selected, position);
        if (!inside)
        {
            return false;
        }

        if (phase == UiPointerPhase.Down)
        {
            SetKeyboardTarget(selected);
        }

        bool consumed;
        try
        {
            consumed = Deliver(selected, new UiPointerInput(pointer.Id, phase, point));
        }
        catch
        {
            pointer.Consumed = phase == UiPointerPhase.Down;
            throw;
        }

        if (consumed && phase == UiPointerPhase.Down)
        {
            pointer.Consumed = true;
            pointer.Target = ReferenceEquals(_targets.GetValueOrDefault(selected.Surface), selected) && Live(selected) ? selected : null;
        }

        return consumed;
    }

    private bool KeyEvent(InputRecord input, KeyData key)
    {
        (InputDeviceId DeviceId, Key Key) identity = (input.DeviceId, key.Key);
        InputWindowId? stateOrigin = input.StateWindowId;
        if (stateOrigin is InputWindowId scope)
        {
            HashSet<(InputDeviceId Device, Key Key)> modifiers = _modifiers.GetValueOrDefault(scope) ?? [];
            _modifiers[scope] = modifiers;
            if (key.IsDown)
            {
                modifiers.Add(identity);
            }
            else
            {
                modifiers.Remove(identity);
            }
        }

        if (_keys.TryGetValue(identity, out KeyOwner? owned))
        {
            if (!key.IsDown)
            {
                _keys.Remove(identity);
            }

            bool knownOrigin = input.WindowId is InputWindowId acquired && _targets.Values.Any(target => target.Origin == acquired && _map.TryGetInputWindowId(target.Window.Id, out InputWindowId live) && live == acquired);
            if (knownOrigin && owned.Target is Target target && Live(target))
            {
                Deliver(target, new UiKeyInput(key.Key, key.IsDown, key.IsRepeat, Modifiers(owned.Origin)));
            }
            else if (owned.Target is Target lost)
            {
                owned.Target = null;
                Deliver(lost, new UiKeyCancelInput(key.Key));
            }

            return true;
        }

        if (!key.IsDown || key.IsRepeat || input.WindowId is not InputWindowId origin)
        {
            return false;
        }

        Target? selected = _keyboardTargets.GetValueOrDefault(origin) ?? _targets.Values.FirstOrDefault(target => target.Origin == origin && (target.Node is null || target.Node == target.Node.PresentationRoot));
        if (selected is null || !Live(selected))
        {
            return false;
        }

        try
        {
            if (!Deliver(selected, new UiKeyInput(key.Key, true, false, Modifiers(origin))))
            {
                return false;
            }

            _keys.Add(identity, new(origin, ReferenceEquals(_targets.GetValueOrDefault(selected.Surface), selected) ? selected : null));
            return true;
        }
        catch
        {
            _keys.TryAdd(identity, new(origin, null));
            throw;
        }
    }

    private UiModifiers Modifiers(InputWindowId origin)
    {
        UiModifiers result = UiModifiers.None;
        if (_modifiers.TryGetValue(origin, out HashSet<(InputDeviceId Device, Key Key)>? keys))
        {
            foreach ((InputDeviceId Device, Key Key) identity in keys)
            {
                result |= identity.Key switch
                {
                    Key.LeftShift or Key.RightShift => UiModifiers.Shift,
                    Key.LeftControl or Key.RightControl => UiModifiers.Control,
                    Key.LeftAlt or Key.RightAlt => UiModifiers.Alt,
                    Key.LeftMeta or Key.RightMeta => UiModifiers.Meta,
                    _ => UiModifiers.None,
                };
            }
        }

        return result;
    }

    private Target? Select(InputWindowId origin, Vector2 point)
    {
        Target? root = _targets.Values.FirstOrDefault(target => target.Origin == origin && UiHostDrawing.Drawable(target.Window.State));
        if (root is null)
        {
            return null;
        }

        if (root.Node is null)
        {
            return root;
        }

        WindowNode? selected = root.Node.PresentationRoot.HitTest(point);
        return _targets.Values.FirstOrDefault(target => target.Node == selected && Live(target));
    }

    private void SetKeyboardTarget(Target target)
    {
        Target? previous = _keyboardTargets.GetValueOrDefault(target.Origin);
        if (previous == target)
        {
            return;
        }

        _keyboardTargets[target.Origin] = target;
        if (previous is not null)
        {
            CancelTarget(previous, removeKeyboard: false);
        }
    }

    private void CancelTarget(Target target, bool removeKeyboard = true)
    {
        foreach (Pointer pointer in _pointers.Values)
        {
            if (pointer.Target == target)
            {
                pointer.Target = null;
            }
        }

        foreach (KeyOwner owner in _keys.Values)
        {
            if (owner.Target == target)
            {
                owner.Target = null;
            }
        }

        if (removeKeyboard && _keyboardTargets.GetValueOrDefault(target.Origin) == target)
        {
            _keyboardTargets.Remove(target.Origin);
        }

        Deliver(target, new UiFocusLostInput());
    }

    private bool Disconnect(InputDeviceId device)
    {
        Pointer[] pointers = _pointers.Where(pair => pair.Key.Device == device).Select(pair => pair.Value).ToArray();
        KeyValuePair<(InputDeviceId Device, Key Key), KeyOwner>[] keys = _keys.Where(pair => pair.Key.Device == device).ToArray();
        foreach ((InputDeviceId Device, int Kind, ulong Contact) identity in _pointers.Keys.Where(identity => identity.Device == device).ToArray())
        {
            _pointers.Remove(identity);
        }

        foreach (KeyValuePair<(InputDeviceId Device, Key Key), KeyOwner> pair in keys)
        {
            _keys.Remove(pair.Key);
        }

        foreach (HashSet<(InputDeviceId Device, Key Key)> scope in _modifiers.Values)
        {
            scope.RemoveWhere(identity => identity.Device == device);
        }

        var errors = new List<Exception>();
        foreach (Pointer pointer in pointers)
        {
            try
            {
                if (pointer.Target is { } target)
                {
                    Deliver(target, new UiPointerInput(pointer.Id, UiPointerPhase.Cancel, default));
                }
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        foreach (KeyValuePair<(InputDeviceId Device, Key Key), KeyOwner> pair in keys)
        {
            try
            {
                if (pair.Value.Target is { } target)
                {
                    Deliver(target, new UiKeyCancelInput(pair.Key.Key));
                }
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        if (errors.Count != 0)
        {
            throw new AggregateException(errors);
        }

        return pointers.Any(pointer => pointer.Consumed) || keys.Length != 0;
    }

    private sealed record Target(IWindow Window, WindowNode? Node, UiSurface Surface, InputWindowId Origin);

    private sealed class Pointer(long id)
    {
        internal long Id { get; } = id;

        internal bool Consumed { get; set; }

        internal Target? Target { get; set; }
    }

    private sealed class KeyOwner(InputWindowId origin, Target? target)
    {
        internal InputWindowId Origin { get; } = origin;

        internal Target? Target { get; set; } = target;
    }
}
