using Lumyte.Input.Abstractions;

namespace Lumyte.UI;

/// <summary>A host-selected physical key transition, independent of committed text.</summary>
/// <param name="Key">The physical key identity.</param>
/// <param name="IsDown">Whether the key is pressed.</param>
/// <param name="IsRepeat">Whether this Down repeats an existing press.</param>
/// <param name="Modifiers">The host-reported modifier state.</param>
public sealed record UiKeyInput(Key Key, bool IsDown, bool IsRepeat = false, UiModifiers Modifiers = UiModifiers.None) : UiInput;
