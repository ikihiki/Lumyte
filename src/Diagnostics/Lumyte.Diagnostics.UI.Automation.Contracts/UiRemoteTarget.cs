namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>A detached registered surface description.</summary>
/// <param name="Id">The Id value.</param>
/// <param name="Name">The Name value.</param>
/// <param name="AllowInput">The AllowInput value.</param>
/// <param name="AllowScreenshots">The AllowScreenshots value.</param>
/// <param name="ParentTargetId">The logical parent target.</param>
/// <param name="PopupId">The explicit popup identity.</param>
/// <param name="ControlGroupId">The shared root control domain.</param>
/// <param name="AutomationCode">A bounded root status such as automation-unavailable.</param>
public sealed record UiRemoteTarget(Guid Id, string Name, bool AllowInput, bool AllowScreenshots, Guid? ParentTargetId = null, Guid? PopupId = null, Guid ControlGroupId = default, string AutomationCode = "");
