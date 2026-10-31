using System.Numerics;

namespace Lumyte.UI.Windowing;

/// <summary>A detached UiWindowCreateRequest value.</summary>
/// <param name="Owner">The Owner value.</param>
/// <param name="ClientSize">The ClientSize value.</param>
/// <param name="Role">The Role value.</param>
/// <param name="InputMode">The InputMode value.</param>
/// <param name="Placement">The Placement value.</param>
/// <param name="Policy">The Policy value.</param>
public sealed record UiWindowCreateRequest(UiWindowId Owner, Vector2 ClientSize, UiWindowRole Role, UiWindowInputMode InputMode, UiWindowPlacement Placement, UiWindowPolicy Policy = UiWindowPolicy.HostedOnly);
