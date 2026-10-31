using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Exercises common function state and subtree context without a page host.</summary>
public sealed class HookTests
{
    /// <summary>Checks standalone roots, nested functions and sibling overrides.</summary>
    [Fact]
    public void NestedFunctionsReadTheNearestProvider()
    {
        var context = new UiContextKey<string>();
        using var surface = new UiSurface(Provide(context, "outer", HookTestUi.Parent(context)));
        Definitions.Panel panel = Assert.IsType<Definitions.Panel>(Body(surface.Root));
        Assert.Equal("outer", Body(panel.Children[0]).AutomationName);
        Assert.Equal("inner", Body(panel.Children[1]).AutomationName);
        Assert.Equal("outer", Body(panel.Children[2]).AutomationName);
        using var direct = new UiSurface(Provide(context, "alone", HookTestUi.Reader(context)));
        Assert.Equal("alone", Body(direct.Root).AutomationName);
    }

    /// <summary>Checks nesting providers on one declaration preserves the inner value.</summary>
    [Fact]
    public void InnerProviderWinsOnTheSameDeclaration()
    {
        var context = new UiContextKey<string>();
        using var surface = new UiSurface(Provide(context, "outer", Provide(context, "inner", HookTestUi.Reader(context))));
        Assert.Equal("inner", Body(surface.Root).AutomationName);
    }

    /// <summary>Checks state including null initializes once and removal creates a fresh instance.</summary>
    /// <param name="useNull">Whether to retain a null value.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommonUseRetainsStateWithoutOwningItsDisposal(bool useNull)
    {
        int calls = 0;
        var values = new List<object?>();
        object? Initialize()
        {
            calls++;
            return useNull ? null : new object();
        }

        using var surface = new UiSurface(HookTestUi.State(Initialize, values.Add));
        surface.Rebuild(HookTestUi.State(Initialize, values.Add));
        surface.UpdateTree();
        Assert.Equal(1, calls);
        Assert.Same(values[0], values[1]);
        surface.Rebuild(Panel());
        surface.UpdateTree();
        surface.Rebuild(HookTestUi.State(Initialize, values.Add));
        surface.UpdateTree();
        Assert.Equal(2, calls);
    }

    /// <summary>Checks reactive recipe rebuilding retains providers and new declarations can replace them.</summary>
    [Fact]
    public void RecipesRetainProvidersAcrossSignalUpdates()
    {
        var context = new UiContextKey<string>();
        var name = Lumyte.Signals.Signal.Create<string?>("first");
        using var surface = new UiSurface(Provide(context, "provided", Panel(automationName: name)[HookTestUi.Reader(context)]));
        name.Value = "second";
        surface.UpdateTree();
        Assert.Equal("provided", Body(Assert.IsType<Definitions.Panel>(surface.Root).Children[0]).AutomationName);
        surface.Rebuild(Provide(context, "replacement", HookTestUi.Reader(context)));
        surface.UpdateTree();
        Assert.Equal("replacement", Body(surface.Root).AutomationName);
    }

    /// <summary>Checks a reentrant host restores context, including after failed construction.</summary>
    [Fact]
    public void NestedHostsAndFailuresRestoreThePreviousFrame()
    {
        var context = new UiContextKey<string>();
        void Run()
        {
            using var nested = new UiSurface(Provide(context, "inner", HookTestUi.Reader(context)));
            Assert.Equal("inner", Body(nested.Root).AutomationName);
            Assert.Throws<InvalidOperationException>(() => new UiSurface(HookTestUi.Reader(context)));
        }

        using var surface = new UiSurface(Provide(context, "outer", HookTestUi.Reentrant(context, Run)));
        Assert.Equal("outer:outer", Body(surface.Root).AutomationName);
        Assert.Throws<InvalidOperationException>(() => UseContext(context));
        Assert.Throws<InvalidOperationException>(() => Use(() => 1));
    }

    /// <summary>Checks direct definitions share a caller frame and require a host when called alone.</summary>
    [Fact]
    public void DirectHelpersShareTheCallingFrame()
    {
        var context = new UiContextKey<string>();
        using var surface = new UiSurface(Provide(context, "outer", HookTestUi.Inline(context)));
        Assert.Equal("outer", Body(surface.Root).AutomationName);
        Assert.Throws<InvalidOperationException>(() => HookTestUi.Definitions.Reader(context));
    }

    /// <summary>Checks separately built roots cannot borrow another thread's provider.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task IndependentRootsRemainIsolatedAsync()
    {
        var context = new UiContextKey<string>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
        {
            string expected = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var surface = new UiSurface(Provide(context, expected, HookTestUi.Reader(context)));
            Assert.Equal(expected, Body(surface.Root).AutomationName);
            Assert.Throws<InvalidOperationException>(() => UseContext(context));
        })));
    }

    /// <summary>Checks late initialization of a connected slot inherits its owner's context.</summary>
    [Fact]
    public void ConnectedSlotsInheritTheOwnerContext()
    {
        var context = new UiContextKey<string>();
        Definitions.Panel panel = Panel();
        using var surface = new UiSurface(Provide(context, "connected", panel));
        panel.Children.Initialize(HookTestUi.Reader(context));
        Assert.Equal("connected", Body(panel.Children[0]).AutomationName);
    }

    /// <summary>Checks different keys with the same value type remain independent.</summary>
    [Fact]
    public void ContextIdentityIsNotJustTheValueType()
    {
        var first = new UiContextKey<string>();
        var second = new UiContextKey<string>();
        Definitions.Panel panel = Panel()[HookTestUi.Reader(first), HookTestUi.Reader(second)];
        using var surface = new UiSurface(Provide(first, "first", Provide(second, "second", panel)));
        Assert.Equal("first", Body(panel.Children[0]).AutomationName);
        Assert.Equal("second", Body(panel.Children[1]).AutomationName);
    }

    private static Widget Body(Widget widget)
    {
        while (widget is UiFunctionWidget function)
        {
            widget = function.Content;
        }

        return widget;
    }
}
