using System.Text.Json;

namespace Lumyte.Interaction;

public static class KeybindingConfiguration
{
    public static KeybindingConfigurationResult Parse(
        string json,
        ContextKeyRegistry contextKeys)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(contextKeys);
        var overrides = new List<KeybindingOverride>();
        var diagnostics = new List<KeybindingConfigurationDiagnostic>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(new(-1, "The keybinding configuration root must be an array."));
                return new(overrides, diagnostics);
            }

            var conditionParser = new ContextConditionParser(contextKeys);
            int index = 0;
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                try
                {
                    overrides.Add(ParseEntry(element, conditionParser));
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                        or FormatException
                        or InvalidOperationException
                        or KeyNotFoundException)
                {
                    diagnostics.Add(new(index, exception.Message));
                }

                index++;
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add(new(-1, exception.Message));
        }

        return new(overrides, diagnostics);
    }

    private static KeybindingOverride ParseEntry(
        JsonElement element,
        ContextConditionParser conditionParser)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A keybinding entry must be an object.");
        }

        string commandText = element.GetProperty("command").GetString()
            ?? throw new FormatException("The command must be a string.");
        bool remove = commandText.StartsWith("-", StringComparison.Ordinal);
        string commandId = remove ? commandText[1..] : commandText;
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        KeyChord? chord = element.TryGetProperty("key", out JsonElement keyElement)
            ? KeyChordParser.Parse(keyElement.GetString()
                ?? throw new FormatException("The key must be a string."))
            : null;
        if (!remove && chord is null)
        {
            throw new FormatException("A key is required when adding a binding.");
        }

        ContextCondition when = element.TryGetProperty("when", out JsonElement whenElement)
            ? conditionParser.Parse(whenElement.GetString()
                ?? throw new FormatException("The when condition must be a string."))
            : ContextCondition.Always;
        return new(commandId, chord, when, remove);
    }
}
