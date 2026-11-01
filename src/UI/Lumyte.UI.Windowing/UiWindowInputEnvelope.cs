using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Windowing;

/// <summary>A detached UiWindowInputEnvelope value.</summary>
/// <param name="Origin">The Origin value.</param>
/// <param name="Input">The Input value.</param>
/// <param name="Device">The acquisition device for key-series ownership; null is a separate synthetic/legacy domain.</param>
public readonly record struct UiWindowInputEnvelope(InputWindowId Origin, UiInput Input, InputDeviceId? Device = null);
