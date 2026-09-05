using Lumyte.Input;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class KeybindingConfigurationTests
{
    [Fact]
    public void ChordParserSupportsSequencesAndModifiers()
    {
        KeyChord chord = KeyChordParser.Parse("ctrl+k ctrl+shift+c");

        Assert.Collection(
            chord.Strokes,
            stroke => Assert.Equal(
                new KeyStroke(Key.K, ModifierKeys.Control),
                stroke),
            stroke => Assert.Equal(
                new KeyStroke(Key.C, ModifierKeys.Control | ModifierKeys.Shift),
                stroke));
    }

    [Fact]
    public void ParsedWhenConditionUsesRegisteredTypedKeys()
    {
        var editor = ContextKey.Create<string?>("editor.active");
        var textInput = ContextKey.Create<bool>("ui.textInputFocused");
        var registry = new ContextKeyRegistry();
        registry.Register(editor);
        registry.Register(textInput);
        ContextCondition condition = new ContextConditionParser(registry)
            .Parse("editor.active == 'scene' && !ui.textInputFocused");
        var context = new InteractionContext();
        context.Set(editor, "scene");
        context.Set(textInput, false);

        bool matches = condition.Evaluate(context);

        Assert.True(matches);
    }

    [Fact]
    public void JsonOverridesReplaceAndRemoveDefaultBindings()
    {
        var save = new Command("editor.save");
        var comment = new Command("editor.comment");
        KeybindingMap defaults = KeybindingMap("Editor")[
            new Keybinding(save, KeyChordParser.Parse("ctrl+s")),
            new Keybinding(comment, KeyChordParser.Parse("ctrl+k ctrl+c"))
        ];
        const string json = """
            [
              { "command": "editor.save", "key": "ctrl+shift+s" },
              { "command": "-editor.comment", "key": "ctrl+k ctrl+c" }
            ]
            """;
        KeybindingConfigurationResult configuration =
            KeybindingConfiguration.Parse(json, new ContextKeyRegistry());

        KeybindingMap effective = KeybindingOverrides.Apply(defaults, configuration.Overrides);

        Assert.Empty(configuration.Diagnostics);
        Keybinding binding = Assert.Single(effective.Bindings);
        Assert.Same(save, binding.Command);
        Assert.Equal(
            new KeyStroke(Key.S, ModifierKeys.Control | ModifierKeys.Shift),
            Assert.Single(binding.Chord.Strokes));
    }

    [Fact]
    public void InvalidEntriesAreDiagnosedWithoutDiscardingValidEntries()
    {
        const string json = """
            [
              { "command": "editor.invalid", "key": "ctrl+not-a-key" },
              { "command": "editor.save", "key": "ctrl+s" }
            ]
            """;

        KeybindingConfigurationResult configuration =
            KeybindingConfiguration.Parse(json, new ContextKeyRegistry());

        KeybindingConfigurationDiagnostic diagnostic = Assert.Single(configuration.Diagnostics);
        Assert.Equal(0, diagnostic.EntryIndex);
        KeybindingOverride valid = Assert.Single(configuration.Overrides);
        Assert.Equal("editor.save", valid.CommandId);
    }

    [Fact]
    public void IdenticalActiveChordsForDifferentCommandsAreReportedAsAConflict()
    {
        KeyChord chord = KeyChordParser.Parse("ctrl+s");
        KeybindingMap map = KeybindingMap("Editor")[
            new Keybinding(new Command("editor.save"), chord),
            new Keybinding(new Command("editor.saveAll"), chord)
        ];

        IReadOnlyList<KeybindingConflict> conflicts = KeybindingConflictDetector.Find(map);

        KeybindingConflict conflict = Assert.Single(conflicts);
        Assert.Equal(
            ["editor.save", "editor.saveAll"],
            conflict.Bindings.Select(binding => binding.Command.Id));
    }
}
