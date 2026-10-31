namespace Lumyte.UI.Automation;

/// <summary>Defines defaults for finite host-clock operation deadlines.</summary>
public sealed record UiAutomationOptions
{
    /// <summary>Gets the positive timeout; defaults to five seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}
