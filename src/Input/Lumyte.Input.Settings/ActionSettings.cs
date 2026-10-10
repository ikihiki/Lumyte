namespace Lumyte.Input.Settings;

/// <summary>Defines action settings.</summary>
public sealed class ActionSettings
{
    /// <summary>Gets or sets id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets kind.</summary>
    public string Kind { get; set; } = "Button";

    /// <summary>Gets or sets sensitivity.</summary>
    public float Sensitivity { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether gets or sets normalize.</summary>
    public bool Normalize { get; set; }

    /// <summary>Gets or sets smoothing seconds.</summary>
    public float SmoothingSeconds { get; set; }
}
