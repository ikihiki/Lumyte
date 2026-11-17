namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>A detached semantic element; UInt64 identities use decimal strings.</summary>
/// <param name="Id">The Id value.</param>
/// <param name="ParentId">The ParentId value.</param>
/// <param name="TestId">The TestId value.</param>
/// <param name="Name">The Name value.</param>
/// <param name="Role">The Role value.</param>
/// <param name="Bounds">The Bounds value.</param>
/// <param name="ClippedBounds">The ClippedBounds value.</param>
/// <param name="IsVisible">The IsVisible value.</param>
/// <param name="IsEnabled">The IsEnabled value.</param>
/// <param name="HitTestVisible">The HitTestVisible value.</param>
/// <param name="IsFocusable">The IsFocusable value.</param>
/// <param name="IsFocused">The IsFocused value.</param>
/// <param name="IsPressed">The IsPressed value.</param>
/// <param name="HasActivePress">The HasActivePress value.</param>
/// <param name="Actions">The Actions value.</param>
/// <param name="IsTransitioning">Whether an element, ancestor or page visual transition gates normal actions.</param>
/// <param name="Value">Optional finite semantic value between zero and one inclusive, not editable text.</param>
/// <param name="IsIndeterminate">Whether the component represents indeterminate progress.</param>
/// <param name="IsChecked">Nullable checked state, including mixed.</param>
/// <param name="IsSelected">Nullable selected state.</param>
/// <param name="SelectionGroup">Selection group UInt64 identity as decimal string.</param>
/// <param name="OptionKey">Ordinal option key.</param>
/// <param name="SelectedKey">Group selected key.</param>
/// <param name="IsExpanded">Accepted expansion state in schema 8.</param>
/// <param name="Numeric">Exact finite numeric metadata, available in schema 6 only.</param>
public sealed record UiRemoteNode(string Id, string? ParentId, string? TestId, string? Name, string Role, UiRemoteRect Bounds, UiRemoteRect ClippedBounds, bool IsVisible, bool IsEnabled, bool HitTestVisible, bool IsFocusable, bool IsFocused, bool IsPressed, bool HasActivePress, string[] Actions, bool IsTransitioning = false, double? Value = null, bool IsIndeterminate = false, bool? IsChecked = null, bool? IsSelected = null, string? SelectionGroup = null, string? OptionKey = null, string? SelectedKey = null, UiRemoteNumericState? Numeric = null, bool? IsExpanded = null);
