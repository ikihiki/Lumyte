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

    /// <summary>Identifies a controller button.</summary>
    /// <param name = "button">The button value.</param>
    /// <returns>The result of the operation.</returns>
    public static InputControl ForButton(ControllerButton button) => new(InputControlKind.ControllerButton, (int)button);

    internal static bool TryRead(InputData data, out InputControl control, out System.Numerics.Vector2 value)
    {
        (control, value) = data switch
        {
            KeyData key => (ForKey(key.Key), new System.Numerics.Vector2(key.IsDown ? 1 : 0, 0)),
            MouseButtonData mouse => (new InputControl(InputControlKind.MouseButton, (int)mouse.Button), new System.Numerics.Vector2(mouse.IsDown ? 1 : 0, 0)),
            ControllerButtonData button => (ForButton(button.Button), new System.Numerics.Vector2(button.IsDown ? 1 : 0, 0)),
            ControllerStickData stick => (new InputControl(InputControlKind.ControllerStick, (int)stick.Stick), stick.Value),
            ControllerTriggerData trigger => (new InputControl(InputControlKind.ControllerTrigger, (int)trigger.Trigger), new System.Numerics.Vector2(trigger.Value, 0)),
            _ => default,
        };
        return data is KeyData or MouseButtonData or ControllerButtonData or ControllerStickData or ControllerTriggerData;
    }
}
