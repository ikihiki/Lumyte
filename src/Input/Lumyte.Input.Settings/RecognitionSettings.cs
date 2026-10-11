namespace Lumyte.Input.Settings;

/// <summary>Defines recognition settings.</summary>
public sealed class RecognitionSettings
{
    /// <summary>Gets or sets id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets context id.</summary>
    public string ContextId { get; set; } = string.Empty;

    /// <summary>Gets or sets kind.</summary>
    public string Kind { get; set; } = "Press";

    /// <summary>Gets or sets performs new.</summary>
    /// <returns>The result of the operation.</returns>
    public List<string> Actions { get; set; } = new();

    /// <summary>Gets or sets window seconds.</summary>
    public double WindowSeconds { get; set; } = 0.2;

    /// <summary>Gets or sets tap count.</summary>
    public int TapCount { get; set; } = 2;
}
