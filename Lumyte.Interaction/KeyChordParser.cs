using Lumyte.Input;

namespace Lumyte.Interaction;

public static class KeyChordParser
{
    public static KeyChord Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        KeyStroke[] strokes =
        [
            .. text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseStroke),
        ];
        return new(strokes);
    }

    private static KeyStroke ParseStroke(string text)
    {
        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new FormatException("A key stroke must contain a key.");
        }

        ModifierKeys modifiers = ModifierKeys.None;
        for (int index = 0; index < parts.Length - 1; index++)
        {
            modifiers |= ParseModifier(parts[index]);
        }

        return new(ParseKey(parts[^1]), modifiers);
    }

    private static ModifierKeys ParseModifier(string value) => value.ToLowerInvariant() switch
    {
        "ctrl" or "control" => ModifierKeys.Control,
        "shift" => ModifierKeys.Shift,
        "alt" => ModifierKeys.Alt,
        "meta" or "cmd" or "command" or "win" => ModifierKeys.Meta,
        _ => throw new FormatException($"Unknown key modifier '{value}'."),
    };

    private static Key ParseKey(string value)
    {
        string normalized = value.Length == 1 && char.IsDigit(value[0])
            ? $"D{value}"
            : value;
        if (!Enum.TryParse(normalized, true, out Key key) || key == Key.Unknown)
        {
            throw new FormatException($"Unknown key '{value}'.");
        }

        return key;
    }
}
