using Lumyte.Composition.Sample;
using Xunit;
using static Lumyte.Composition.Sample.Compose;

namespace Lumyte.Composition.Tests;

/// <summary>Exercises generated function declarations using an independent host in another assembly.</summary>
public sealed class FunctionTests
{
    /// <summary>Checks the host controls body execution and omitted or explicit-null parameters retain their meaning.</summary>
    [Fact]
    public void HostControlsExecutionAndDefaults()
    {
        int calls = 0;
        var function = (Compose.Definitions.FunctionWidget)Greeting(onBuild: () => calls++, key: "sample");
        Assert.Equal(0, calls);
        Assert.Equal("sample", function.Key);
        Assert.Equal("hello", Assert.IsType<Compose.Definitions.Text>(function.Evaluate()).Content);
        Assert.Equal(1, calls);
        var cleared = (Compose.Definitions.FunctionWidget)Greeting(label: null);
        Assert.Null(Assert.IsType<Compose.Definitions.Text>(cleared.Evaluate()).Content);
    }

    /// <summary>Checks the passive recipe creates a new declaration with the original concrete values and identity.</summary>
    [Fact]
    public void RecipeReconstructsTheDeclaration()
    {
        var first = (Compose.Definitions.FunctionWidget)Greeting("saved", key: "same");
        var second = (Compose.Definitions.FunctionWidget)first.Factory!();
        Assert.NotSame(first, second);
        Assert.Equal("same", second.Key);
        Assert.Equal("saved", Assert.IsType<Compose.Definitions.Text>(second.Evaluate()).Content);
    }

    /// <summary>Checks generic functions execute through statically generated closed wrappers.</summary>
    [Fact]
    public void GenericFunctionUsesTheClosedValueType()
    {
        var function = (Compose.Definitions.FunctionWidget)Typed(7);
        Assert.Equal("7", Assert.IsType<Compose.Definitions.Text>(function.Evaluate()).Content);
    }
}
