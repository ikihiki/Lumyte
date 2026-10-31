using Microsoft.CodeAnalysis;
using Xunit;

namespace Lumyte.Composition.Tests;

/// <summary>Checks static construction functions independently of the UI library.</summary>
public sealed partial class GeneratorTests
{
    private const string FunctionHost = "using Lumyte.Composition; [ComposeFunctionAdapter(typeof(Function))] public class Node { public string? Key { get; set; } } public abstract class Function : Node { protected Function() { } protected abstract Node Build(); } public static partial class Compose { public static partial class Definitions { ";

    /// <summary>Checks functions support defaults, concrete delegates, return subtypes and generic constraints.</summary>
    [Fact]
    public void FunctionsCompileAgainstAnIndependentHost()
    {
        string source = FunctionHost + "[Composable] public static Node Item<T>(T value, decimal price = 1.25m, System.DayOfWeek? day = System.DayOfWeek.Friday, float fraction = 0.25f, double missing = double.NaN, ulong large = ulong.MaxValue, string? title = null, System.Action? click = null) where T : notnull => new Node(); } } public class Usage { public Node Build() => Compose.Item(1, click: () => {}, key: \"demo\"); }";
        GeneratorDriverRunResult result = Run(source, out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        Assert.Single(result.GeneratedTrees);
    }

    /// <summary>Checks unsupported function shapes receive a generator error.</summary>
    /// <param name="declaration">The malformed definition.</param>
    [Theory]
    [InlineData("[Composable] public static void Item() { }")]
    [InlineData("[Composable] public static int Item() => 0;")]
    [InlineData("[Composable] private static Node Item() => new();")]
    [InlineData("[Composable] public static Node Item(ref int value) => new();")]
    [InlineData("[Composable] public static Node Item(params int[] values) => new();")]
    [InlineData("[Composable] public static Node Item(string key) => new();")]
    [InlineData("[Composable] public static Node Item(System.Span<int> value) => new();")]
    public void UnsupportedFunctionsAreDiagnosed(string declaration)
    {
        GeneratorDriverRunResult result = Run(FunctionHost + declaration + " } }", out _);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "LYC001");
        Assert.Empty(result.GeneratedTrees);
    }

    /// <summary>Checks overloads and class/function factory collisions are diagnosed without duplicate generated files.</summary>
    [Fact]
    public void FunctionFactoriesCannotShareAName()
    {
        string source = FunctionHost + "[Composable] public static Node Item() => new(); [Composable] public static Node Item(int value) => new(); } }";
        GeneratorDriverRunResult result = Run(source, out _);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "LYC001");
        Assert.Single(result.GeneratedTrees);
    }

    /// <summary>Checks method factory names can explicitly disambiguate overloads.</summary>
    [Fact]
    public void ExplicitFactoryNamesDisambiguateOverloads()
    {
        string source = FunctionHost + "[Composable] public static Node Item() => new(); [Composable(Name = \"Number\")] public static Node Item(int value) => new(); } } public class Usage { public Node Build() => Compose.Number(1); }";
        GeneratorDriverRunResult result = Run(source, out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        Assert.Equal(2, result.GeneratedTrees.Length);
    }
}
