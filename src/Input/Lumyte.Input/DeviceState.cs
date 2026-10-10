using System.Collections.Immutable;
using System.Numerics;

namespace Lumyte.Input;

internal sealed class DeviceState(InputDeviceKind kind)
{
    internal InputDeviceKind Kind { get; } = kind;

    internal bool Connected { get; set; } = true;

    internal bool Focused { get; set; }

    internal Vector2 Position { get; set; }

    internal bool[] Keys { get; } = kind == InputDeviceKind.Keyboard ? new bool[(int)Key.IntlYen + 1] : [];

    internal bool[] MouseButtons { get; } = kind == InputDeviceKind.Mouse ? new bool[(int)MouseButton.X2 + 1] : [];

    internal bool[] ControllerButtons { get; } = kind == InputDeviceKind.Controller ? new bool[(int)ControllerButton.Select + 1] : [];

    internal Vector2[] Sticks { get; } = kind == InputDeviceKind.Controller ? new Vector2[2] : [];

    internal float[] Triggers { get; } = kind == InputDeviceKind.Controller ? new float[2] : [];

    internal Dictionary<TouchContactId, TouchData> Touches { get; } = [];

    internal Dictionary<PenPointerId, PenData> Pens { get; } = [];

    internal ImmutableHashSet<TouchContactId> UsedContacts { get; private set; } = [];

    internal ImmutableHashSet<PenPointerId> UsedPointers { get; private set; } = [];

    internal static void CheckEnum(Key value) => CheckRange((int)value, (int)Key.IntlYen);

    internal static void CheckEnum(MouseButton value) => CheckRange((int)value, (int)MouseButton.X2);

    internal static void CheckEnum(ControllerButton value) => CheckRange((int)value, (int)ControllerButton.Select);

    internal static void CheckEnum(ControllerStick value) => CheckRange((int)value, (int)ControllerStick.Right);

    internal static void CheckEnum(ControllerTrigger value) => CheckRange((int)value, (int)ControllerTrigger.Right);

    internal static void CheckEnum(TouchPhase value) => CheckRange((int)value, (int)TouchPhase.Canceled);

    internal static void CheckEnum(PenPhase value) => CheckRange((int)value, (int)PenPhase.Canceled);

    internal static void CheckEnum(InputDeviceKind value) => CheckRange((int)value, (int)InputDeviceKind.Pen);

    internal static void CheckEnum(InputDeviceIdentityKind value) => CheckRange((int)value, (int)InputDeviceIdentityKind.Logical);

    internal DeviceState Clone(bool includeIdentityHistory = true)
    {
        var copy = new DeviceState(Kind)
        {
            Connected = Connected,
            Focused = Focused,
            Position = Position,
        };
        Keys.CopyTo(copy.Keys, 0);
        MouseButtons.CopyTo(copy.MouseButtons, 0);
        ControllerButtons.CopyTo(copy.ControllerButtons, 0);
        Sticks.CopyTo(copy.Sticks, 0);
        Triggers.CopyTo(copy.Triggers, 0);
        if (includeIdentityHistory)
        {
            copy.UsedContacts = UsedContacts;
            copy.UsedPointers = UsedPointers;
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
                if (key.Key != Key.Unknown && (!key.IsRepeat || Keys[(int)key.Key]))
                {
                    Keys[(int)key.Key] = key.IsDown;
                }

                break;
            case MouseButtonData mouse:
                MouseButtons[(int)mouse.Button] = mouse.IsDown;
                break;
            case MouseMoveData move:
                Position = move.Position;
                break;
            case ControllerButtonData button:
                ControllerButtons[(int)button.Button] = button.IsDown;
                break;
            case ControllerStickData stick:
                Sticks[(int)stick.Stick] = stick.Value;
                break;
            case ControllerTriggerData trigger:
                Triggers[(int)trigger.Trigger] = trigger.Value;
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
        for (int i = 0; i < Keys.Length; i++)
        {
            if (Keys[i])
            {
                applied.Add(new KeyData((Key)i, false, false));
            }
        }

        for (int i = 0; i < MouseButtons.Length; i++)
        {
            if (MouseButtons[i])
            {
                applied.Add(new MouseButtonData((MouseButton)i, false));
            }
        }

        for (int i = 0; i < ControllerButtons.Length; i++)
        {
            if (ControllerButtons[i])
            {
                applied.Add(new ControllerButtonData((ControllerButton)i, false));
            }
        }

        for (int i = 0; i < Sticks.Length; i++)
        {
            if (Sticks[i] != Vector2.Zero)
            {
                applied.Add(new ControllerStickData((ControllerStick)i, Vector2.Zero));
            }
        }

        for (int i = 0; i < Triggers.Length; i++)
        {
            if (Triggers[i] != 0)
            {
                applied.Add(new ControllerTriggerData((ControllerTrigger)i, 0));
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

        Array.Clear(Keys);
        Array.Clear(MouseButtons);
        Array.Clear(ControllerButtons);
        Array.Clear(Sticks);
        Array.Clear(Triggers);
        Touches.Clear();
        Pens.Clear();
    }

    // All supported non-flags enums are contiguous int values starting at zero.
    private static void CheckRange(int value, int maximum)
    {
        if ((uint)value > (uint)maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
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
            if (UsedContacts.Contains(touch.ContactId))
            {
                throw new ArgumentException("Contact identifiers cannot be reused.");
            }

            UsedContacts = UsedContacts.Add(touch.ContactId);
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
            if (pen.IsInContact || UsedPointers.Contains(pen.PointerId))
            {
                throw new ArgumentException("A pointer must enter hovering and cannot reuse its identifier.");
            }

            UsedPointers = UsedPointers.Add(pen.PointerId);
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
