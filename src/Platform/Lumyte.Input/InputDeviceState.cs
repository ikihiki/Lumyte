using System.Collections.ObjectModel;
using System.Numerics;

namespace Lumyte.Input;

/// <summary>Provides an immutable snapshot of a device's current state.</summary>
public sealed class InputDeviceState
{
    private readonly DeviceState _state;

    internal InputDeviceState(DeviceState state)
    {
        _state = state.Clone();
    }

    /// <summary>Gets a value indicating whether the device is connected.</summary>
    public bool IsConnected => _state.Connected;

    /// <summary>Gets a value indicating whether the input target has focus.</summary>
    public bool IsFocused => _state.Focused;

    /// <summary>Gets the last mouse position in logical pixels.</summary>
    public Vector2 MousePosition
    {
        get
        {
            Require(InputDeviceKind.Mouse);
            return _state.Position;
        }
    }

    /// <summary>Gets the active touch contacts.</summary>
    public IReadOnlyDictionary<TouchContactId, TouchData> TouchContacts
    {
        get
        {
            Require(InputDeviceKind.Touch);
            return new ReadOnlyDictionary<TouchContactId, TouchData>(_state.Touches);
        }
    }

    /// <summary>Gets the in-range pen pointers, including hovering pointers.</summary>
    public IReadOnlyDictionary<PenPointerId, PenData> PenPointers
    {
        get
        {
            Require(InputDeviceKind.Pen);
            return new ReadOnlyDictionary<PenPointerId, PenData>(_state.Pens);
        }
    }

    /// <summary>Checks a physical key's current pressed state.</summary>
    /// <param name="key">The physical key.</param>
    /// <returns>Whether the key is pressed.</returns>
    public bool IsDown(Key key)
    {
        Require(InputDeviceKind.Keyboard);
        DeviceState.CheckEnum(key);
        return _state.Keys.Contains(key);
    }

    /// <summary>Checks a mouse button's current pressed state.</summary>
    /// <param name="button">The mouse button.</param>
    /// <returns>Whether the button is pressed.</returns>
    public bool IsDown(MouseButton button)
    {
        Require(InputDeviceKind.Mouse);
        DeviceState.CheckEnum(button);
        return _state.MouseButtons.Contains(button);
    }

    /// <summary>Checks a controller button's current pressed state.</summary>
    /// <param name="button">The controller button.</param>
    /// <returns>Whether the button is pressed.</returns>
    public bool IsDown(ControllerButton button)
    {
        Require(InputDeviceKind.Controller);
        DeviceState.CheckEnum(button);
        return _state.ControllerButtons.Contains(button);
    }

    /// <summary>Gets the normalized stick position before dead-zone processing.</summary>
    /// <param name="stick">The stick to inspect.</param>
    /// <returns>The position with right and up positive.</returns>
    public Vector2 GetStick(ControllerStick stick)
    {
        Require(InputDeviceKind.Controller);
        DeviceState.CheckEnum(stick);
        return _state.Sticks.GetValueOrDefault(stick);
    }

    /// <summary>Gets the normalized trigger value.</summary>
    /// <param name="trigger">The trigger to inspect.</param>
    /// <returns>The value from zero to one.</returns>
    public float GetTrigger(ControllerTrigger trigger)
    {
        Require(InputDeviceKind.Controller);
        DeviceState.CheckEnum(trigger);
        return _state.Triggers.GetValueOrDefault(trigger);
    }

    private void Require(InputDeviceKind kind)
    {
        if (_state.Kind != kind)
        {
            throw new InvalidOperationException("The query does not match the device kind.");
        }
    }
}
