namespace Lumyte.Input.Settings;

/// <summary>Defines input action settings.</summary>
public sealed class InputActionSettings
{
    /// <summary>Gets or sets performs new.</summary>
    /// <returns>The result of the operation.</returns>
    public List<ActionSettings> Actions { get; set; } = new();

    /// <summary>Gets or sets performs new.</summary>
    /// <returns>The result of the operation.</returns>
    public List<BindingSettings> Bindings { get; set; } = new();

    /// <summary>Gets or sets performs new.</summary>
    /// <returns>The result of the operation.</returns>
    public List<ContextSettings> Contexts { get; set; } = new();

    /// <summary>Gets or sets performs new.</summary>
    /// <returns>The result of the operation.</returns>
    public List<RecognitionSettings> Recognitions { get; set; } = new();

    /// <summary>Gets or sets buffer lifetime seconds.</summary>
    public double BufferLifetimeSeconds { get; set; } = 0.2;

    /// <summary>Gets or sets buffer max entries.</summary>
    public int BufferMaxEntries { get; set; } = 32;
}
