namespace Lumyte.Diagnostics.UI.Automation.Tests;

// Frozen C2 reader contract, independent of current response DTOs.
internal sealed record OldNode(string Id, string? ParentId, string? TestId, string? Name, string Role, OldRect Bounds, OldRect ClippedBounds, bool IsVisible, bool IsEnabled, bool HitTestVisible, bool IsFocusable, bool IsFocused, bool IsPressed, bool HasActivePress, string[] Actions, bool IsTransitioning = false, double? Value = null, bool IsIndeterminate = false);
