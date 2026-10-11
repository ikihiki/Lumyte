namespace Lumyte.Input.Settings;

/// <summary>Defines binding settings.</summary>
public sealed class BindingSettings
{
    /// <summary>Gets or sets id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets action id.</summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>Gets or sets context id.</summary>
    public string ContextId { get; set; } = string.Empty;

    /// <summary>Gets or sets control kind.</summary>
    public string ControlKind { get; set; } = "Key";

    /// <summary>Gets or sets control.</summary>
    public string Control { get; set; } = "Space";

    /// <summary>Gets or sets scale x.</summary>
    public float ScaleX { get; set; } = 1;

    /// <summary>Gets or sets scale y.</summary>
    public float ScaleY { get; set; }

    /// <summary>Gets or sets press threshold.</summary>
    public float PressThreshold { get; set; } = 0.5f;

    /// <summary>Gets or sets release threshold.</summary>
    public float ReleaseThreshold { get; set; } = 0.4f;
}
