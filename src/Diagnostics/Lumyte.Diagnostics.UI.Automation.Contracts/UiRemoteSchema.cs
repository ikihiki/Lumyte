using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>Validates and normalizes the closed version-one UI wire schema.</summary>
public static class UiRemoteSchema
{
    private static readonly HashSet<string> _keys = new(StringComparer.Ordinal)
    {
        "Unknown",
        "A",
        "B",
        "C",
        "D",
        "E",
        "F",
        "G",
        "H",
        "I",
        "J",
        "K",
        "L",
        "M",
        "N",
        "O",
        "P",
        "Q",
        "R",
        "S",
        "T",
        "U",
        "V",
        "W",
        "X",
        "Y",
        "Z",
        "Digit0",
        "Digit1",
        "Digit2",
        "Digit3",
        "Digit4",
        "Digit5",
        "Digit6",
        "Digit7",
        "Digit8",
        "Digit9",
        "Escape",
        "Tab",
        "CapsLock",
        "Space",
        "Enter",
        "Backspace",
        "LeftShift",
        "RightShift",
        "LeftControl",
        "RightControl",
        "LeftAlt",
        "RightAlt",
        "LeftMeta",
        "RightMeta",
        "ArrowUp",
        "ArrowDown",
        "ArrowLeft",
        "ArrowRight",
        "Insert",
        "Delete",
        "Home",
        "End",
        "PageUp",
        "PageDown",
        "F1",
        "F2",
        "F3",
        "F4",
        "F5",
        "F6",
        "F7",
        "F8",
        "F9",
        "F10",
        "F11",
        "F12",
        "F13",
        "F14",
        "F15",
        "F16",
        "F17",
        "F18",
        "F19",
        "F20",
        "F21",
        "F22",
        "F23",
        "F24",
        "Minus",
        "Equal",
        "LeftBracket",
        "RightBracket",
        "Backslash",
        "Semicolon",
        "Quote",
        "Backquote",
        "Comma",
        "Period",
        "Slash",
        "NumLock",
        "PrintScreen",
        "ScrollLock",
        "Pause",
        "Numpad0",
        "Numpad1",
        "Numpad2",
        "Numpad3",
        "Numpad4",
        "Numpad5",
        "Numpad6",
        "Numpad7",
        "Numpad8",
        "Numpad9",
        "NumpadMultiply",
        "NumpadDivide",
        "NumpadAdd",
        "NumpadSubtract",
        "NumpadDecimal",
        "NumpadEnter",
        "IntlBackslash",
        "IntlRo",
        "IntlYen",
    };

    /// <summary>Parses, validates and copies a plan; unknown and duplicate fields are rejected.</summary>
    /// <param name="json">At most 32768 characters of JSON.</param>
    /// <returns>A normalized plan owned by the caller.</returns>
    public static UiRemotePlan ParsePlan(string json)
    {
        if (json is null || json.Length > 32768)
        {
            throw new JsonException("Invalid plan size.");
        }

        using var document = JsonDocument.Parse(json, new() { MaxDepth = 16 });
        RejectDuplicates(document.RootElement);
        UiRemotePlan plan = JsonSerializer.Deserialize(json, UiAutomationJsonContext.Default.UiRemotePlan) ?? throw new JsonException("Missing plan.");
        Require(plan.SchemaVersion is >= 1 and <= 3 && plan.Locator is not null && plan.Kind is "click" or "focus" or "press" or "count" or "expect" or "snapshot" or "screenshot" or "fill" or "expect-value" or "ime-candidates" or "ime-select" or "ime-page" or "ime-finalize" or "ime-abort");
        Require(plan.SchemaVersion >= 2 || !document.RootElement.TryGetProperty("text", out _));
        Require(plan.SchemaVersion == 3 || (!document.RootElement.TryGetProperty("candidate", out _) && !document.RootElement.TryGetProperty("candidateIndex", out _)));
        Require(plan.Kind is "snapshot" or "screenshot" or "ime-candidates" or "ime-select" or "ime-page" or "ime-finalize" or "ime-abort" ? plan.Locator.Length == 0 : plan.Locator.Length is >= 1 and <= 8);
        Require(plan.SchemaVersion >= 2 || plan.Locator.All(step => step?.Role is not ("textbox" or "ime-candidates")));
        Require(plan.SchemaVersion == 3 || plan.Locator.All(step => step?.Role != "ime-candidates"));
        foreach (UiRemoteLocatorStep step in plan.Locator)
        {
            Require(step is not null);
            if (step.Kind == "test-id")
            {
                Require(!string.IsNullOrWhiteSpace(step.Value) && step.Value.Length <= 256 && step.Role is null && step.Name is null && !step.IncludeHidden);
            }
            else
            {
                Require(step.Kind == "role" && step.Role is "button" or "group" or "textbox" or "ime-candidates" && step.Value is null && (step.Name is null || step.Name.Length <= 256));
            }
        }

        Require(plan.Kind == "click" || plan.Position is null);
        if (plan.Position is { } position)
        {
            Require(double.IsFinite(position.X) && double.IsFinite(position.Y) && float.IsFinite((float)position.X) && float.IsFinite((float)position.Y) && position.X >= 0 && position.Y >= 0);
        }

        if (plan.Kind == "press")
        {
            Require(plan.Key is not null && _keys.Contains(plan.Key));
            string[] modifiers = plan.Modifiers ?? [];
            Require(modifiers.Length <= 4 && modifiers.Distinct(StringComparer.Ordinal).Count() == modifiers.Length && modifiers.All(value => value is "Shift" or "Control" or "Alt" or "Meta"));
            plan = plan with { Modifiers = modifiers.Order(StringComparer.Ordinal).ToArray() };
        }
        else
        {
            Require(plan.Key is null && plan.Modifiers is null);
        }

        if (plan.Kind == "expect")
        {
            Require(plan.Predicate is "visible" or "hidden" or "enabled" or "focused" or "count");
            Require(plan.Predicate == "count" ? plan.Count is >= 0 : plan.Count is null);
        }
        else
        {
            Require(plan.Predicate is null && plan.Count is null);
        }

        bool textOperation = plan.Kind is "fill" or "expect-value";
        bool candidateAction = plan.Kind is "ime-select" or "ime-page" or "ime-finalize" or "ime-abort";
        Require((plan.SchemaVersion >= 2 || (!textOperation && plan.Text is null)) && (plan.SchemaVersion == 3 || (!candidateAction && plan.Kind != "ime-candidates" && plan.Candidate is null && plan.CandidateIndex is null)));
        Require(textOperation ? plan.Text is not null && plan.Text.Length <= 32768 : plan.Text is null);
        if (plan.Text is { } text)
        {
            try
            {
                _ = new System.Text.UTF8Encoding(false, true).GetByteCount(text);
            }
            catch (System.Text.EncoderFallbackException)
            {
                throw new JsonException("Invalid text encoding.");
            }
        }

        Require(candidateAction == (plan.Candidate is not null));
        if (plan.Candidate is { } candidate)
        {
            Require(candidate.SessionId != Guid.Empty && Counter(candidate.SessionGeneration) && Counter(candidate.ElementGeneration) && Counter(candidate.Revision));
        }

        Require(plan.Kind is "ime-select" or "ime-page" ? plan.CandidateIndex is >= 0 : plan.CandidateIndex is null);
        return plan;
    }

    /// <summary>Validates and canonically serializes a caller plan, copying arrays through the closed schema.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>Normalized camelCase JSON.</returns>
    public static string SerializePlan(UiRemotePlan plan) => JsonSerializer.Serialize(ParsePlan(JsonSerializer.Serialize(plan, UiAutomationJsonContext.Default.UiRemotePlan)), UiAutomationJsonContext.Default.UiRemotePlan);

    /// <summary>Tests whether a plan can send input.</summary>
    /// <param name="plan">A validated plan.</param>
    /// <returns>Whether input authority is required.</returns>
    public static bool IsAction(UiRemotePlan plan) => plan.Kind is "click" or "focus" or "press" or "fill" or "ime-select" or "ime-page" or "ime-finalize" or "ime-abort";

    private static bool Counter(string value) => value is not null && value.Length <= 20 && ulong.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong parsed) && parsed != 0 && parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) == value;

    private static void Require([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
        {
            throw new JsonException("The plan violates the closed UI automation schema.");
        }
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                Require(names.Add(property.Name));
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in value.EnumerateArray())
            {
                RejectDuplicates(child);
            }
        }
    }
}
