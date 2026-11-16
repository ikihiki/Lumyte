namespace Lumyte.UI;

/// <summary>Host-committed Unicode text, independent of physical keys and IME preedit.</summary>
/// <param name="Text">The committed text.</param>
public sealed record UiCommittedTextInput(string Text) : UiInput;
