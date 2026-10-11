namespace Lumyte.Input.Settings;

/// <summary>Defines device correction settings.</summary>
public sealed class DeviceCorrectionSettings
{
    /// <summary>Gets or sets center x.</summary>
    public float CenterX { get; set; }

    /// <summary>Gets or sets center y.</summary>
    public float CenterY { get; set; }

    /// <summary>Gets or sets dead zone.</summary>
    public float DeadZone { get; set; } = 0.15f;

    /// <summary>Gets or sets pressure exponent.</summary>
    public float PressureExponent { get; set; } = 1;

    /// <summary>Gets or sets the smoothing time constant; zero disables smoothing.</summary>
    public float SmoothingSeconds { get; set; }
}
