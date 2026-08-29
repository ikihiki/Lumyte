using Lumyte.Input;

namespace Lumyte.Interaction;

public sealed record KeyStroke(Key Key, ModifierKeys Modifiers = ModifierKeys.None);
