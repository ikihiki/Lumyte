namespace Lumyte.Input;

/// <summary>Persistent input bindings and analog thresholds.</summary>
public sealed class InputSettings
{
    /// <summary>Gets or sets context IDs mapping action IDs to physical bindings; an empty array unbinds an action.</summary>
    public Dictionary<string, Dictionary<string, string[]>> Bindings { get; set; } = [];

    /// <summary>Gets or sets left-stick thresholds.</summary>
    public DeadZoneSettings LeftStick { get; set; } = new();

    /// <summary>Gets or sets right-stick thresholds.</summary>
    public DeadZoneSettings RightStick { get; set; } = new();

    /// <summary>Gets or sets left-trigger thresholds.</summary>
    public DeadZoneSettings LeftTrigger { get; set; } = new() { Inner = 0.05f };

    /// <summary>Gets or sets right-trigger thresholds.</summary>
    public DeadZoneSettings RightTrigger { get; set; } = new() { Inner = 0.05f };
}
