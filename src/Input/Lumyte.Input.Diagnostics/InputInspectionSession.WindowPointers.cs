using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Actions;

namespace Lumyte.Input.Diagnostics;

/// <summary>Keeps typed pointer diagnostic values in their explicit window coordinate space.</summary>
public sealed partial class InputInspectionSession
{
    private readonly Dictionary<(InputSystem System, InputDeviceId Device, InputWindowId Window, DeviceTargetKind Kind, ulong Pointer), InputOverrideTarget> _windowPointerTargets = [];

    /// <summary>Gets an observed mouse target in an explicit window coordinate space.</summary>
    /// <param name="system">The inspected system.</param>
    /// <param name="device">The mouse device.</param>
    /// <param name="window">The observed coordinate space.</param>
    /// <param name="wheel">Whether to select wheel delta.</param>
    /// <returns>The observed target.</returns>
    public InputOverrideTarget GetMouseTarget(InputSystem system, InputDeviceId device, InputWindowId window, bool wheel = false) => GetWindowPointerTarget(system, device, window, wheel ? DeviceTargetKind.MouseWheel : DeviceTargetKind.MousePosition, 0);

    /// <summary>Gets an observed touch target in an explicit window scope.</summary>
    /// <param name="system">The inspected system.</param>
    /// <param name="device">The touch device.</param>
    /// <param name="window">The observed coordinate space.</param>
    /// <param name="contact">The active contact.</param>
    /// <param name="pressure">Whether to select pressure.</param>
    /// <returns>The active target.</returns>
    public InputOverrideTarget GetTouchTarget(InputSystem system, InputDeviceId device, InputWindowId window, TouchContactId contact, bool pressure = false) => GetWindowPointerTarget(system, device, window, pressure ? DeviceTargetKind.TouchPressure : DeviceTargetKind.TouchPosition, contact.Value);

    /// <summary>Gets an observed pen target in an explicit window scope.</summary>
    /// <param name="system">The inspected system.</param>
    /// <param name="device">The pen device.</param>
    /// <param name="window">The observed coordinate space.</param>
    /// <param name="pointer">The active pointer.</param>
    /// <param name="pressure">Whether to select pressure.</param>
    /// <returns>The active target.</returns>
    public InputOverrideTarget GetPenTarget(InputSystem system, InputDeviceId device, InputWindowId window, PenPointerId pointer, bool pressure = false) => GetWindowPointerTarget(system, device, window, pressure ? DeviceTargetKind.PenPressure : DeviceTargetKind.PenPosition, pointer.Value);

    private InputOverrideTarget GetWindowPointerTarget(InputSystem system, InputDeviceId device, InputWindowId window, DeviceTargetKind kind, ulong pointer)
    {
        Check();
        return _windowPointerTargets.TryGetValue((system, device, window, kind, pointer), out InputOverrideTarget target) && _targets.ContainsKey(target) ? target : throw new KeyNotFoundException("The window pointer target is unobserved or retired.");
    }

    private InputData ApplyWindowPointer(InputSystem system, InputSourceEvent input, InputWindowId window, TimeSpan now)
    {
        InputData data = input.Data;
        (DeviceTargetKind kind, ulong pointer, bool terminal) = data switch
        {
            MouseMoveData => (DeviceTargetKind.MousePosition, 0UL, false),
            MouseWheelData => (DeviceTargetKind.MouseWheel, 0UL, false),
            TouchData touch => (DeviceTargetKind.TouchPosition, touch.ContactId.Value, touch.Phase is TouchPhase.Ended or TouchPhase.Canceled),
            PenData pen => (DeviceTargetKind.PenPosition, pen.PointerId.Value, pen.Phase is PenPhase.Left or PenPhase.Canceled),
            _ => (DeviceTargetKind.None, 0UL, false),
        };
        if (kind == DeviceTargetKind.None)
        {
            return data;
        }

        if (terminal || input.WindowId != window || _retiredWindows.Contains((system, window)))
        {
            CancelWindowPointers(system, input.DeviceId, window, pointer);
            return data;
        }

        Vector2 position = data switch
        {
            MouseMoveData move => move.Position,
            MouseWheelData wheel => wheel.Delta,
            TouchData touch => touch.Position,
            PenData pen => pen.Position,
            _ => default,
        };
        InputData result = Apply(kind, position, data);
        if (data is TouchData touchData)
        {
            result = Apply(DeviceTargetKind.TouchPressure, new(touchData.Pressure ?? -1, 0), result);
        }
        else if (data is PenData penData)
        {
            result = Apply(DeviceTargetKind.PenPressure, new(penData.Pressure ?? -1, 0), result);
        }

        return result;

        InputData Apply(DeviceTargetKind valueKind, Vector2 physical, InputData template)
        {
            (InputSystem System, InputDeviceId Device, InputWindowId Window, DeviceTargetKind Kind, ulong Pointer) identity = (system, input.DeviceId, window, valueKind, pointer);
            if (!_windowPointerTargets.TryGetValue(identity, out InputOverrideTarget target))
            {
                if (_windowPointerTargets.Count >= 8192)
                {
                    throw new InvalidOperationException("Window pointer inspection capacity reached.");
                }

                target = new(Guid.NewGuid());
                _windowPointerTargets.Add(identity, target);
                _targets.Add(target, TargetEntry.ForDeviceData(system, input.DeviceId, valueKind, pointer, valueKind is DeviceTargetKind.TouchPressure or DeviceTargetKind.PenPressure ? ActionValueKind.Axis1D : ActionValueKind.Axis2D) with { WindowId = window });
                _deviceDataStates.Add(target, new());
            }

            DeviceDataState state = _deviceDataStates[target];
            state.Physical = physical;
            state.Template = template;
            state.Effective = EffectiveDeviceValue(target, physical, now);
            return CreateTypedData(valueKind, template, state.Effective);
        }
    }

    private void AdvanceWindowPointers(InputSystem system, TimeSpan now, List<InputScopedEvent> output)
    {
        foreach (KeyValuePair<(InputSystem System, InputDeviceId Device, InputWindowId Window, DeviceTargetKind Kind, ulong Pointer), InputOverrideTarget> pair in _windowPointerTargets.Where(pair => ReferenceEquals(pair.Key.System, system)).ToArray())
        {
            if (!_targets.ContainsKey(pair.Value))
            {
                continue;
            }

            DeviceDataState state = _deviceDataStates[pair.Value];
            Vector2 desired = EffectiveDeviceValue(pair.Value, state.Physical, now);
            if (desired == state.Effective || state.Template is null)
            {
                continue;
            }

            state.Effective = desired;
            InputData data = state.Template;
            if (data is TouchData or PenData)
            {
                DeviceTargetKind positionKind = data is TouchData ? DeviceTargetKind.TouchPosition : DeviceTargetKind.PenPosition;
                DeviceTargetKind pressureKind = data is TouchData ? DeviceTargetKind.TouchPressure : DeviceTargetKind.PenPressure;
                InputOverrideTarget position = _windowPointerTargets[(system, pair.Key.Device, pair.Key.Window, positionKind, pair.Key.Pointer)];
                InputOverrideTarget pressure = _windowPointerTargets[(system, pair.Key.Device, pair.Key.Window, pressureKind, pair.Key.Pointer)];
                data = CreateTypedData(positionKind, data, _deviceDataStates[position].Effective, synthetic: true);
                data = CreateTypedData(pressureKind, data, _deviceDataStates[pressure].Effective, synthetic: true);
            }
            else
            {
                data = CreateTypedData(pair.Key.Kind, data, desired, synthetic: true);
            }

            output.Add(new(new(pair.Key.Device, pair.Key.Window, null, data), pair.Key.Window));
        }
    }

    private void CancelWindowPointers(InputSystem system, InputDeviceId device, InputWindowId window, ulong? pointer = null)
    {
        foreach (KeyValuePair<(InputSystem System, InputDeviceId Device, InputWindowId Window, DeviceTargetKind Kind, ulong Pointer), InputOverrideTarget> pair in _windowPointerTargets.Where(pair => ReferenceEquals(pair.Key.System, system) && pair.Key.Device == device && pair.Key.Window == window && (pointer is null || pair.Key.Pointer == pointer)).ToArray())
        {
            InvalidateTarget(pair.Value, "window-pointer-canceled");
            _targets.Remove(pair.Value);
            _windowPointerTargets.Remove(pair.Key);
            _deviceDataStates.Remove(pair.Value);
            _activeDeviceTargets.Remove(pair.Value);
            _dirtyDeviceTargets.Remove(pair.Value);
        }
    }
}
