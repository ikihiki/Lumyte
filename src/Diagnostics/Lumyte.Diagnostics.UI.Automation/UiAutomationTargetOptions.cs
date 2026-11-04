namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Defines bounded opt-in automation configuration.</summary>
public sealed record UiAutomationTargetOptions
{
    /// <summary>Gets a value indicating whether explicit text mutation is enabled.</summary>
    public bool AllowTextInput { get; init; }

    /// <summary>Gets a value indicating whether explicit text and candidate inspection is enabled.</summary>
    public bool AllowTextInspection { get; init; }

    /// <summary>Gets a value indicating whether AllowInput is permitted.</summary>
    public bool AllowInput { get; init; } = false;

    /// <summary>Gets a value indicating whether AllowScreenshots is permitted.</summary>
    public bool AllowScreenshots { get; init; } = false;
}
