using Lumyte.Diagnostics.UI.Automation;

namespace Lumyte.UI.Application.Diagnostics;

/// <summary>Configures opt-in diagnostic targets and bounded jobs.</summary>
public sealed record UiApplicationDiagnosticsOptions
{
    /// <summary>Gets the unique lowercase ASCII primary target name.</summary>
    public string TargetName { get; init; } = "main";

    /// <summary>Gets explicit permissions, all disabled by default.</summary>
    public UiAutomationTargetOptions Target { get; init; } = new();

    /// <summary>Gets the job and artifact limits.</summary>
    public UiAutomationDiagnosticsOptions Runtime { get; init; } = new();
}
