using Lumyte.Input.Abstractions;

namespace Lumyte.UI;

/// <summary>Cancels a key's recognition while retaining consumption until its physical Up.</summary>
/// <param name="Key">The key series to cancel.</param>
public sealed record UiKeyCancelInput(Key Key) : UiInput;
