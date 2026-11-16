using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

// Frozen v4 reader: intentionally independent of the production context and DTOs.
internal sealed record FrozenV4Node(string Id, string? ParentId, string? TestId, string? Name, string Role, FrozenV4Rect Bounds, FrozenV4Rect ClippedBounds, bool IsVisible, bool IsEnabled, bool HitTestVisible, bool IsFocusable, bool IsFocused, bool IsPressed, bool HasActivePress, string[] Actions, bool IsTransitioning = false, double? Value = null, bool IsIndeterminate = false, bool? IsChecked = null, bool? IsSelected = null, string? SelectionGroup = null, string? OptionKey = null, string? SelectedKey = null);

internal sealed record FrozenV4Rect(double X, double Y, double Width, double Height);

internal sealed record FrozenV4Snapshot(Guid SurfaceId, string Sequence, FrozenV4Node[] Nodes);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(FrozenV4Snapshot))]
internal partial class FrozenV4Context : JsonSerializerContext
{
}
