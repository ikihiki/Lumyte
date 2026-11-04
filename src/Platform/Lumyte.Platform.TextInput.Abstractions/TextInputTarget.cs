using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines text input target.</summary>
/// <param name = "FocusWindow">The focus window.</param>
/// <param name = "PresentationWindow">The presentation window.</param>
/// <param name = "SurfaceId">The surface id.</param>
/// <param name = "EditorId">The editor id.</param>
public sealed record TextInputTarget(WindowId FocusWindow, WindowId PresentationWindow, Guid SurfaceId, Guid EditorId);
