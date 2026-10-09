namespace Lumyte.Input;

/// <summary>Inner and outer thresholds for normalized analog input.</summary>
public sealed class DeadZoneSettings
{
    /// <summary>Gets or sets the neutral threshold, defaulting to 0.15.</summary>
    public float Inner { get; set; } = 0.15f;

    /// <summary>Gets or sets the saturation threshold, defaulting to one.</summary>
    public float Outer { get; set; } = 1;
}
