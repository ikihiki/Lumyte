namespace Lumyte.Input.Settings;

/// <summary>Defines input processing settings.</summary>
public sealed class InputProcessingSettings
{
    /// <summary>Gets or sets performs new.</summary>
    /// <returns>The result of the operation.</returns>
    public Dictionary<string, DeviceCorrectionSettings> DeviceProfiles { get; set; } = new();

    /// <summary>Gets or sets the touch joystick radius in logical pixels.</summary>
    public float TouchRadius { get; set; } = 100;

    /// <summary>Gets or sets the minimum completed swipe distance in logical pixels.</summary>
    public float SwipeDistance { get; set; } = 80;
}
