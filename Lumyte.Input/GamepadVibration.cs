namespace Lumyte.Input;

public readonly record struct GamepadVibration(float LowFrequency, float HighFrequency)
{
    public GamepadVibration Clamp() => new(
        Math.Clamp(LowFrequency, 0, 1),
        Math.Clamp(HighFrequency, 0, 1));
}
