namespace Lumyte.Input.Settings;

/// <summary>Defines context settings.</summary>
public sealed class ContextSettings
{
    /// <summary>Gets or sets id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional parent context identifier.</summary>
    public string? ParentId { get; set; }

    /// <summary>Gets or sets local action definitions.</summary>
    public List<ActionSettings> Actions { get; set; } = new();

    /// <summary>Gets or sets priority.</summary>
    public int Priority { get; set; }

    /// <summary>Gets or sets a value indicating whether gets or sets exclusive.</summary>
    public bool Exclusive { get; set; }
}
