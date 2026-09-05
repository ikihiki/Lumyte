namespace Lumyte.Interaction;

public sealed record PlayerInputOptions
{
    public required int Player { get; init; }

    public bool AcceptsGamepadJoin { get; init; } = true;

    public string? BindingOverridesJson { get; init; }

    public float MouseMovementThreshold { get; init; } = 2;

    public float GamepadStickThreshold { get; init; } = 0.25f;

    public float GamepadTriggerThreshold { get; init; } = 0.1f;
}
