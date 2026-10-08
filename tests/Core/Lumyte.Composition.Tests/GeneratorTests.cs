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
    [InlineData("[Composable] public partial class Grid<T> where T : allows ref struct { }")]
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

    /// <summary>Tests generic settings, content, operations, and constraint propagation.</summary>
    /// <param name="constraint">The component type constraint.</param>
    /// <param name="argument">The supplied type argument.</param>
    /// <param name="value">The setting expression.</param>
    [Theory]
    [InlineData("where T : struct", "int", "1")]
    [InlineData("where T : class, new()", "object", "new object()")]
    [InlineData("where T : unmanaged", "int", "1")]
    public void GenericComponentCompiles(string constraint, string argument, string value)
    {
        string source = "using System.Collections.Generic; using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { [Composable] public partial class ListView<T> " + constraint + " { [ComposeParameter] public T Value { get; set; } = default!; [ComposeContent] public IReadOnlyList<T> Items { get; set; } = []; [ComposeAction] private static void Set(ListView<T> target, T value) => target.Value = value; } } } public class Usage { public object Build() => Compose.ListView<" + argument + ">(value: " + value + ", with: [Compose.ListViewFactory<" + argument + ">().Set(" + value + ")])[" + value + "]; }";
        GeneratorDriverRunResult result = Run(source, out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Tests multiple type arguments and constraints referring to another type parameter.</summary>
    [Fact]
    public void MultipleTypeArgumentsCompile()
    {
        const string Source = "using System; using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { [Composable] public partial class Pair<T, U> where T : class where U : T, new() { [ComposeParameter] public U Value { get; set; } = new(); [ComposeAction] private static void Set(Pair<T, U> target, U value) => target.Value = value; } } } public class Usage { public object Build() => Compose.Pair<object, object>(with: [Compose.PairFactory<object, object>().Set(new object())]); }";
        GeneratorDriverRunResult result = Run(Source, out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Tests that generated entry points enforce the original type constraints.</summary>
    [Fact]
    public void InvalidGenericArgumentIsRejected()
    {
        const string Source = "using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { [Composable] public partial class ListView<T> where T : struct { } } } public class Usage { public object Build() => Compose.ListView<string>(); }";
        Run(Source, out Compilation output);
        Assert.Contains(output.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0453");
    }

    /// <summary>Tests collisions between a generic factory accessor and another component factory.</summary>
    [Fact]
    public void GenericAccessorCollisionIsDiagnosed()
    {
        const string Source = "using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { [Composable] public partial class Item<T> { } [Composable(Name = \"ItemFactory\")] public partial class Other { } } }";
        GeneratorDriverRunResult result = Run(Source, out _);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "LYC001");
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
