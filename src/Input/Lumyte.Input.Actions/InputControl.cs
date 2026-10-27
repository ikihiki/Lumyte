using Lumyte.Input.Abstractions;

namespace Lumyte.Input.Actions;
/// <summary>Defines struct.</summary>
/// <param name = "Kind">The Kind value.</param>
/// <param name = "Index">The Index value.</param>
public readonly record struct InputControl(InputControlKind Kind, int Index)
{
    /// <summary>Identifies a physical keyboard key.</summary>
    /// <param name = "key">The key value.</param>
    /// <returns>The result of the operation.</returns>
    public static InputControl ForKey(Key key) => new(InputControlKind.Key, (int)key);

    /// <summary>Identifies a mouse button.</summary>
    /// <param name="button">The mouse button.</param>
    /// <returns>The corresponding mouse control.</returns>
    public static InputControl ForMouseButton(MouseButton button) => new(InputControlKind.MouseButton, (int)button);

    /// <summary>Identifies a controller button.</summary>
    /// <param name="button">The controller button.</param>
    /// <returns>The corresponding controller control.</returns>
    public static InputControl ForControllerButton(ControllerButton button) => new(InputControlKind.ControllerButton, (int)button);

    /// <summary>Identifies a controller stick.</summary>
    /// <param name="stick">The controller stick.</param>
    /// <returns>The corresponding stick control.</returns>
    public static InputControl ForControllerStick(ControllerStick stick) => new(InputControlKind.ControllerStick, (int)stick);

    /// <summary>Identifies a controller trigger.</summary>
    /// <param name="trigger">The controller trigger.</param>
    /// <returns>The corresponding trigger control.</returns>
    public static InputControl ForControllerTrigger(ControllerTrigger trigger) => new(InputControlKind.ControllerTrigger, (int)trigger);

    internal static bool TryRead(InputData data, out InputControl control, out System.Numerics.Vector2 value)
    {
        (control, value) = data switch
        {
            KeyData key => (ForKey(key.Key), new System.Numerics.Vector2(key.IsDown ? 1 : 0, 0)),
            MouseButtonData mouse => (ForMouseButton(mouse.Button), new System.Numerics.Vector2(mouse.IsDown ? 1 : 0, 0)),
            ControllerButtonData button => (ForControllerButton(button.Button), new System.Numerics.Vector2(button.IsDown ? 1 : 0, 0)),
            ControllerStickData stick => (ForControllerStick(stick.Stick), stick.Value),
            ControllerTriggerData trigger => (ForControllerTrigger(trigger.Trigger), new System.Numerics.Vector2(trigger.Value, 0)),
            _ => default,
        };
        return data is KeyData or MouseButtonData or ControllerButtonData or ControllerStickData or ControllerTriggerData;
    }
}
