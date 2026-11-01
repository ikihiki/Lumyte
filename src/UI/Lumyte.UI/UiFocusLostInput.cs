namespace Lumyte.UI;

/// <summary>Cancels every pointer owned by the surface without running layout or user callbacks.</summary>
public sealed record UiFocusLostInput : UiInput;
