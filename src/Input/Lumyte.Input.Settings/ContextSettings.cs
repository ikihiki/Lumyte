namespace Lumyte.Input.Settings;

/// <summary>Defines context settings.</summary>
public sealed class ContextSettings
{
    /// <summary>Gets or sets id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets priority.</summary>
    public int Priority { get; set; }

    /// <summary>Gets or sets a value indicating whether gets or sets exclusive.</summary>
    public bool Exclusive { get; set; }
}
