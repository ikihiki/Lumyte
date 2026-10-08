using System.Numerics;

namespace Lumyte.Input;

internal sealed class DeviceState(InputDeviceKind kind)
{
    internal InputDeviceKind Kind { get; } = kind;

    internal bool Connected { get; set; } = true;

    internal bool Focused { get; set; }

    internal Vector2 Position { get; set; }

    internal HashSet<Key> Keys { get; } = [];

    internal HashSet<MouseButton> MouseButtons { get; } = [];

    internal HashSet<ControllerButton> ControllerButtons { get; } = [];

    internal Dictionary<ControllerStick, Vector2> Sticks { get; } = [];

    internal Dictionary<ControllerTrigger, float> Triggers { get; } = [];

    internal Dictionary<TouchContactId, TouchData> Touches { get; } = [];

    internal Dictionary<PenPointerId, PenData> Pens { get; } = [];

    internal HashSet<TouchContactId> UsedContacts { get; } = [];

    internal HashSet<PenPointerId> UsedPointers { get; } = [];

    internal static void CheckEnum<T>(T value)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    internal DeviceState Clone()
    {
        var copy = new DeviceState(Kind)
        {
            Connected = Connected,
            Focused = Focused,
            Position = Position,
        };
        copy.Keys.UnionWith(Keys);
        copy.MouseButtons.UnionWith(MouseButtons);
        copy.ControllerButtons.UnionWith(ControllerButtons);
        copy.UsedContacts.UnionWith(UsedContacts);
        copy.UsedPointers.UnionWith(UsedPointers);
        foreach (KeyValuePair<ControllerStick, Vector2> pair in Sticks)
        {
            copy.Sticks.Add(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<ControllerTrigger, float> pair in Triggers)
        {
            copy.Triggers.Add(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<TouchContactId, TouchData> pair in Touches)
        {
            copy.Touches.Add(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<PenPointerId, PenData> pair in Pens)
        {
            copy.Pens.Add(pair.Key, pair.Value);
        }

        return copy;
    }

    internal void Apply(InputData data, List<InputData> applied)
    {
        ArgumentNullException.ThrowIfNull(data);
        Validate(data);
        if (data is FocusData focus)
        {
            Focused = focus.IsFocused;
            applied.Add(data);
            if (!Focused)
            {
                Neutralize(applied);
            }

            return;
        }

        if (!Focused)
        {
            return;
        }

        switch (data)
        {
            case KeyData key:
                if (key.Key != Key.Unknown && (!key.IsRepeat || Keys.Contains(key.Key)))
                {
                    Set(Keys, key.Key, key.IsDown);
                }

                break;
            case MouseButtonData mouse:
                Set(MouseButtons, mouse.Button, mouse.IsDown);
                break;
            case MouseMoveData move:
                Position = move.Position;
                break;
            case ControllerButtonData button:
                Set(ControllerButtons, button.Button, button.IsDown);
                break;
            case ControllerStickData stick:
                Sticks[stick.Stick] = stick.Value;
                break;
            case ControllerTriggerData trigger:
                Triggers[trigger.Trigger] = trigger.Value;
                break;
            case TouchData touch:
                ApplyTouch(touch);
                break;
            case PenData pen:
                ApplyPen(pen);
                break;
        }

        applied.Add(data);
    }

    internal void Neutralize(List<InputData> applied)
    {
        foreach (Key key in Keys.Order())
        {
            applied.Add(new KeyData(key, false, false));
        }

        foreach (MouseButton button in MouseButtons.Order())
        {
            applied.Add(new MouseButtonData(button, false));
        }

        foreach (ControllerButton button in ControllerButtons.Order())
        {
            applied.Add(new ControllerButtonData(button, false));
        }

        foreach (KeyValuePair<ControllerStick, Vector2> pair in Sticks.OrderBy(pair => pair.Key))
        {
            if (pair.Value != Vector2.Zero)
            {
                applied.Add(new ControllerStickData(pair.Key, Vector2.Zero));
            }
        }

        foreach (KeyValuePair<ControllerTrigger, float> pair in Triggers.OrderBy(pair => pair.Key))
        {
            if (pair.Value != 0)
            {
                applied.Add(new ControllerTriggerData(pair.Key, 0));
            }
        }

        foreach (TouchData touch in Touches.Values.OrderBy(touch => touch.ContactId.Value))
        {
            applied.Add(touch with { Phase = TouchPhase.Canceled, Pressure = touch.Pressure.HasValue ? 0 : null });
        }

        foreach (PenData pen in Pens.Values.OrderBy(pen => pen.PointerId.Value))
        {
            applied.Add(pen with
            {
                Phase = PenPhase.Canceled,
                Pressure = pen.Pressure.HasValue ? 0 : null,
                IsInContact = false,
                Buttons = PenButtons.None,
            });
        }

        Keys.Clear();
        MouseButtons.Clear();
        ControllerButtons.Clear();
        Sticks.Clear();
        Triggers.Clear();
        Touches.Clear();
        Pens.Clear();
    }

    private static void Set<T>(HashSet<T> values, T value, bool isDown)
    {
        if (isDown)
        {
            values.Add(value);
        }
        else
        {
            values.Remove(value);
        }
    }

    private static void PositionValid(Vector2 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentException("Coordinates must be finite.");
        }
    }

    private static void PressureValid(float? pressure)
    {
        if (pressure is float value && (!float.IsFinite(value) || value < 0 || value > 1))
        {
            throw new ArgumentException("Pressure must be null or between zero and one.");
        }
    }

    private void Validate(InputData data)
    {
        switch (data)
        {
            case FocusData:
                return;
            case KeyData key when Kind == InputDeviceKind.Keyboard:
                CheckEnum(key.Key);
                if (key.IsRepeat && !key.IsDown)
                {
                    throw new ArgumentException("Only key-down events can repeat.");
                }

                return;
            case MouseButtonData button when Kind == InputDeviceKind.Mouse:
                CheckEnum(button.Button);
                return;
            case MouseMoveData move when Kind == InputDeviceKind.Mouse:
                PositionValid(move.Position);
                return;
            case MouseWheelData wheel when Kind == InputDeviceKind.Mouse:
                PositionValid(wheel.Delta);
                return;
            case ControllerButtonData button when Kind == InputDeviceKind.Controller:
                CheckEnum(button.Button);
                return;
            case ControllerStickData stick when Kind == InputDeviceKind.Controller:
                CheckEnum(stick.Stick);
                PositionValid(stick.Value);
                if (Math.Abs(stick.Value.X) > 1 || Math.Abs(stick.Value.Y) > 1)
                {
                    throw new ArgumentException("Stick axes must be between minus one and one.");
                }

                return;
            case ControllerTriggerData trigger when Kind == InputDeviceKind.Controller:
                CheckEnum(trigger.Trigger);
                PressureValid(trigger.Value);
                return;
            case TouchData touch when Kind == InputDeviceKind.Touch:
                CheckEnum(touch.Phase);
                PositionValid(touch.Position);
                PressureValid(touch.Pressure);
                if (touch.ContactId.Value == 0)
                {
                    throw new ArgumentException("A contact identifier cannot be zero.");
                }

                return;
            case PenData pen when Kind == InputDeviceKind.Pen:
                CheckEnum(pen.Phase);
                PositionValid(pen.Position);
                PressureValid(pen.Pressure);
                if (pen.PointerId.Value == 0 || (pen.Buttons & ~(PenButtons.Barrel | PenButtons.SecondaryBarrel)) != 0)
                {
                    throw new ArgumentException("Invalid pen identifier or buttons.");
                }

                return;
            default:
                throw new ArgumentException("The input does not match the device kind or is system-generated.");
        }
    }

    private void ApplyTouch(TouchData touch)
    {
        if (touch.Phase == TouchPhase.Began)
        {
            if (!UsedContacts.Add(touch.ContactId))
            {
                throw new ArgumentException("Contact identifiers cannot be reused.");
            }
        }
        else if (!Touches.ContainsKey(touch.ContactId))
        {
            throw new ArgumentException("A touch must begin before it can change or end.");
        }

        if (touch.Phase is TouchPhase.Ended or TouchPhase.Canceled)
        {
            Touches.Remove(touch.ContactId);
        }
        else
        {
            Touches[touch.ContactId] = touch;
        }
    }

    private void ApplyPen(PenData pen)
    {
        if (pen.Phase == PenPhase.Entered)
        {
            if (pen.IsInContact || !UsedPointers.Add(pen.PointerId))
            {
                throw new ArgumentException("A pointer must enter hovering and cannot reuse its identifier.");
            }
        }
        else
        {
            if (!Pens.TryGetValue(pen.PointerId, out PenData? previous))
            {
                throw new ArgumentException("A pen must enter before sending input.");
            }

            bool valid = pen.Phase switch
            {
                PenPhase.Down => !previous.IsInContact && pen.IsInContact,
                PenPhase.Up => previous.IsInContact && !pen.IsInContact,
                PenPhase.Moved => previous.IsInContact == pen.IsInContact,
                PenPhase.Left or PenPhase.Canceled => !pen.IsInContact,
                _ => false,
            };
            if (!valid)
            {
                throw new ArgumentException("Invalid pen contact transition.");
            }
        }

        if (!pen.IsInContact && pen.Pressure is not null and not 0)
        {
            throw new ArgumentException("A hovering or ended pen must have zero or unknown pressure.");
        }

        if (pen.Phase is PenPhase.Left or PenPhase.Canceled)
        {
            Pens.Remove(pen.PointerId);
        }
        else
        {
            Pens[pen.PointerId] = pen;
        }
    }
}
