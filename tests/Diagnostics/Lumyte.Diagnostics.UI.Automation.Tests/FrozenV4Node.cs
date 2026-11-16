namespace Lumyte.Diagnostics.UI.Automation.Tests;

// Frozen v4 contract, independent of production DTOs.
internal sealed record FrozenV4Node(string Id, string? ParentId, string? TestId, string? Name, string Role, FrozenV4Rect Bounds, FrozenV4Rect ClippedBounds, bool IsVisible, bool IsEnabled, bool HitTestVisible, bool IsFocusable, bool IsFocused, bool IsPressed, bool HasActivePress, string[] Actions, bool IsTransitioning = false, double? Value = null, bool IsIndeterminate = false, bool? IsChecked = null, bool? IsSelected = null, string? SelectionGroup = null, string? OptionKey = null, string? SelectedKey = null);
