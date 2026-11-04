using Lumyte.Input.Abstractions;

namespace Lumyte.Input.Internal;

internal sealed class InputSeriesOwners
{
    private Dictionary<(InputDeviceId Device, int Kind, ulong Control), (InputWindowId? Owner, bool Blocked)> Owners { get; set; } = [];

    private HashSet<(InputDeviceId Device, int Kind, ulong Control)> Used { get; set; } = [];

    private HashSet<InputWindowId> Closed { get; set; } = [];

    internal bool IsClosed(InputWindowId window) => Closed.Contains(window);

    internal InputSeriesOwners Clone() => new() { Owners = new(Owners), Closed = new(Closed), Used = new(Used) };

    internal (InputWindowId? Owner, bool Suppress) Resolve(InputSourceEvent input)
    {
        if (input.OriginDeviceId is not null)
        {
            return (input.StateWindowId, input.Data is not (FocusData { IsFocused: false } or WindowInputClosedData) && input.StateWindowId is InputWindowId scope && Closed.Contains(scope));
        }

        if (input.Data is FocusData { IsFocused: false } or WindowInputClosedData)
        {
            if (input.Data is WindowInputClosedData)
            {
                if (input.WindowId is not InputWindowId closed)
                {
                    throw new ArgumentException("A window close requires an origin.");
                }

                Closed.Add(closed);
            }

            foreach ((InputDeviceId Device, int Kind, ulong Control) key in Owners.Keys.ToArray())
            {
                if (key.Device == input.DeviceId && Owners[key].Owner == input.WindowId)
                {
                    Owners[key] = (input.WindowId, true);
                }
            }

            return (input.WindowId, false);
        }

        (int kind, ulong control, bool start, bool end) = input.Data switch
        {
            KeyData key => (0, (ulong)key.Key, key.IsDown && !key.IsRepeat, !key.IsDown),
            MouseButtonData mouse => (1, (ulong)mouse.Button, mouse.IsDown, !mouse.IsDown),
            MouseButtonCanceledData cancel => (1, (ulong)cancel.Button, false, true),
            TouchData touch => (2, touch.ContactId.Value, touch.Phase == TouchPhase.Began, touch.Phase is TouchPhase.Ended or TouchPhase.Canceled),
            PenData pen => (3, pen.PointerId.Value, pen.Phase == PenPhase.Entered, pen.Phase is PenPhase.Left or PenPhase.Canceled),
            _ => (-1, 0UL, false, false),
        };
        (InputDeviceId Device, int Kind, ulong Control) identity = (Device: input.DeviceId, Kind: kind, Control: control);
        bool retired = input.WindowId is InputWindowId origin && Closed.Contains(origin);
        if (kind < 0)
        {
            if (input.Data is MouseMoveData)
            {
                foreach (KeyValuePair<(InputDeviceId Device, int Kind, ulong Control), (InputWindowId? Owner, bool Blocked)> pair in Owners)
                {
                    if (pair.Key.Device == input.DeviceId && pair.Key.Kind == 1)
                    {
                        return (pair.Value.Owner, pair.Value.Blocked);
                    }
                }
            }

            return (input.WindowId, retired);
        }

        if (start && kind is 2 or 3 && !retired && !Used.Add(identity))
        {
            throw new ArgumentException("Contact/pointer IDs cannot be reused within a device lifetime.");
        }

        if (Owners.TryGetValue(identity, out (InputWindowId? Owner, bool Blocked) owner))
        {
            if (end)
            {
                Owners.Remove(identity);
            }

            return (owner.Owner, owner.Blocked);
        }

        InputWindowId? target = input.WindowId;
        if (kind == 1)
        {
            foreach (KeyValuePair<(InputDeviceId Device, int Kind, ulong Control), (InputWindowId? Owner, bool Blocked)> pair in Owners)
            {
                if (pair.Key.Device == input.DeviceId && pair.Key.Kind == 1)
                {
                    target = pair.Value.Owner;
                    retired |= pair.Value.Blocked;
                    break;
                }
            }
        }

        if (start)
        {
            Owners.Add(identity, (target, retired));
        }

        return (target, retired);
    }
}
