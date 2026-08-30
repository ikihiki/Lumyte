using Xunit;

namespace Lumyte.Interaction.Tests;

public sealed class InteractionContextTests
{
    [Fact]
    public void TypedValuesDriveComposedConditions()
    {
        var running = ContextKey.Create<bool>("game.running");
        var editor = ContextKey.Create<string?>("editor.active");
        var context = new InteractionContext();
        context.Set(running, true);
        context.Set(editor, "scene");
        ContextCondition condition = running.Is(true) & editor.Is("scene");

        bool actual = condition.Evaluate(context);

        Assert.True(actual);
    }

    [Fact]
    public void SettingAChangedValuePublishesItsKeyAndValues()
    {
        var focused = ContextKey.Create<bool>("ui.focused");
        var context = new InteractionContext();
        ContextValueChangedEventArgs? change = null;
        context.ValueChanged += (_, eventArgs) => change = eventArgs;
        context.Set(focused, false);

        context.Set(focused, true);

        Assert.NotNull(change);
        Assert.Same(focused, change.Key);
        Assert.Equal(false, change.PreviousValue);
        Assert.Equal(true, change.Value);
    }

    [Fact]
    public void ConditionsHaveAStableConfigurationExpression()
    {
        var editor = ContextKey.Create<string?>("editor.active");
        var textInput = ContextKey.Create<bool>("ui.textInputFocused");
        ContextCondition condition = editor.Is("scene") & textInput.IsNot(true);

        string expression = condition.ToExpression();

        Assert.Equal("(editor.active == 'scene') && (!(ui.textInputFocused == true))", expression);
    }
}
