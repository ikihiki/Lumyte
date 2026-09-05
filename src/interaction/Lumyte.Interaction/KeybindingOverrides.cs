namespace Lumyte.Interaction;

public static class KeybindingOverrides
{
    public static KeybindingMap Apply(
        KeybindingMap defaults,
        IEnumerable<KeybindingOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(overrides);
        var effective = new List<Keybinding>(defaults.Bindings);
        foreach (KeybindingOverride item in overrides)
        {
            if (item.Remove)
            {
                effective.RemoveAll(binding =>
                    binding.Command.Id == item.CommandId
                    && (item.Chord is null || ChordsEqual(binding.Chord, item.Chord)));
                continue;
            }

            Command command = effective
                .Select(binding => binding.Command)
                .FirstOrDefault(candidate => candidate.Id == item.CommandId)
                ?? new Command(item.CommandId);
            effective.RemoveAll(binding => binding.Command.Id == item.CommandId);
            effective.Add(new(command, item.Chord!) { When = item.When });
        }

        return KeybindingMap.CreateEffective(defaults.Name, effective);
    }

    private static bool ChordsEqual(KeyChord left, KeyChord right) =>
        left.Strokes.SequenceEqual(right.Strokes);
}
