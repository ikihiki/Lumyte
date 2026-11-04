namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Defines bounded opt-in automation configuration.</summary>
public sealed record UiAutomationDiagnosticsOptions
{
    /// <summary>Gets the MaximumTargets limit.</summary>
    public int MaximumTargets { get; init; } = 16;

    /// <summary>Gets the MaximumJobs limit.</summary>
    public int MaximumJobs { get; init; } = 64;

    /// <summary>Gets the MaximumRunningJobs limit.</summary>
    public int MaximumRunningJobs { get; init; } = 4;

    /// <summary>Gets the MaximumArtifactBytes limit.</summary>
    public long MaximumArtifactBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Gets the MaximumStoredArtifactBytes limit.</summary>
    public long MaximumStoredArtifactBytes { get; init; } = 32 * 1024 * 1024;

    /// <summary>Gets the Retention limit.</summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromSeconds(120);
}
