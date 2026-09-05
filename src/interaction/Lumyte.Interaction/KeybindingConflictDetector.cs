namespace Lumyte.Interaction;

public static class KeybindingConflictDetector
{
    public static IReadOnlyList<KeybindingConflict> Find(KeybindingMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return
        [
            .. map.Bindings
                .GroupBy(binding => new
                {
                    Chord = FormatChord(binding.Chord),
                    When = binding.When.ToExpression(),
                })
                .Where(group => group.Select(binding => binding.Command.Id).Distinct().Skip(1).Any())
                .Select(group => new KeybindingConflict([.. group])),
        ];
    }

    private static string FormatChord(KeyChord chord) => string.Join(
        " ",
        chord.Strokes.Select(stroke => $"{(int)stroke.Modifiers}:{(int)stroke.Key}"));
}
