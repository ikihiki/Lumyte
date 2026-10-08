using Lumyte.Composition.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Lumyte.Composition.Tests;

/// <summary>Tests generator diagnostics against compilations containing malformed declarations.</summary>
public sealed class GeneratorTests
{
    /// <summary>Tests that unsupported definitions produce a deliberate diagnostic.</summary>
    /// <param name="member">The malformed component declaration.</param>
    [Theory]
    [InlineData("public partial class Grid { [ComposeAction] public static void Column(object target, int value) { } }")]
    [InlineData("[Composable] public class Grid { }")]
    [InlineData("[Composable] public partial class Grid<T> { }")]
    [InlineData("[Composable] public partial class Grid { [ComposeAction] public void Column(object target, int value) { } }")]
    [InlineData("[Composable] public partial class Grid { [ComposeAction] public static int Column(object target, int value) => 0; }")]
    [InlineData("[Composable] public partial class Grid { [ComposeParameter] public int With { get; set; } }")]
    [InlineData("[Composable] public partial class Grid { [ComposeContent] public List<object> Children { get; set; } = []; }")]
    [InlineData("[Composable] public partial class Grid { public required int Value { get; init; } }")]
    [InlineData("[Composable] public partial class Grid { [ComposeAction] public static void Column(object target, ref int value) { } }")]
    [InlineData("[Composable] public partial class Grid { [ComposeAction] public static void Column(object target, int value = 0) { } }")]
    [InlineData("[Composable] public partial class Grid { [ComposeAction] public static void Column<T>(object target, T value) { } }")]
    [InlineData("[Composable] public partial class Grid { [ComposeAction] public static void Invoke(object target) { } }")]
    [InlineData("[Composable] public partial class Grid { [ComposeAction] public static void Column(int target, int value) { } }")]
    public void InvalidDeclarationIsDiagnosed(string member)
    {
        string source = "using System.Collections.Generic; using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { " + member + " } }";
        GeneratorDriverRunResult result = Run(source, out _);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "LYC001");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "AD0001");
    }

    /// <summary>Tests diagnostics for duplicate generated factory properties.</summary>
    [Fact]
    public void DuplicateFactoryNameIsDiagnosed()
    {
        const string Source = "using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { [Composable(Name = \"Same\")] public partial class First { } [Composable(Name = \"Same\")] public partial class Second { } } }";
        GeneratorDriverRunResult result = Run(Source, out _);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "LYC001");
    }

    /// <summary>Tests rejection of a missing required argument by generated delegate signature.</summary>
    [Fact]
    public void RequiredArgumentCannotBeOmitted()
    {
        const string Source = "using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { [Composable] public partial class Item { [ComposeParameter] public required string Text { get; init; } } } } public class Usage { public object Build() => Compose.Item(); }";
        Run(Source, out Compilation output);
        Assert.Contains(output.GetDiagnostics(), diagnostic => diagnostic.Id == "CS7036");
    }

    /// <summary>Tests that attached extensions do not apply to another factory delegate type.</summary>
    [Fact]
    public void AttachedExtensionIsSpecificToItsFactory()
    {
        const string Source = "using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { [Composable] public partial class Grid { [ComposeAction] public static void Column(object target, int value) { } } [Composable] public partial class Text { } } } public class Usage { public object Build() => Compose.Text.Column(1); }";
        Run(Source, out Compilation output);
        Assert.Contains(output.GetDiagnostics(), diagnostic => diagnostic.Id == "CS1929");
    }

    private static GeneratorDriverRunResult Run(string source, out Compilation output)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        string[] platformPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = platformPaths.Select(path => MetadataReference.CreateFromFile(path));
        references = references.Append(MetadataReference.CreateFromFile(typeof(ComposableAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            [CSharpSyntaxTree.ParseText(source, options)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new CompositionGenerator().AsSourceGenerator()], parseOptions: options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out output, out _);
        return driver.GetRunResult();
    }
}
