namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Defines bounded opt-in automation configuration.</summary>
public sealed record UiAutomationTargetOptions
{
    /// <summary>Gets a value indicating whether AllowInput is permitted.</summary>
    public bool AllowInput { get; init; } = false;

    /// <summary>Gets a value indicating whether AllowScreenshots is permitted.</summary>
    public bool AllowScreenshots { get; init; } = false;
}
