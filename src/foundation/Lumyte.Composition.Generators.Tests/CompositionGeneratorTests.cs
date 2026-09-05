using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Lumyte.Composition.Generators.Tests;

public sealed class CompositionGeneratorTests
{
    private const string ComponentSource = """
        using System;
        using System.Collections.Generic;

        namespace Lumyte.Composition
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ComposableAttribute : Attribute
            {
                public string? Factory { get; set; }
                public string? Name { get; set; }
            }

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class ComposeParameterAttribute : Attribute
            {
            }

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class ComposeContentAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Assembly)]
            public sealed class CompositionDefaultsAttribute(string factoryClass) : Attribute;

            public readonly struct Optional<T>
            {
                public bool HasValue => false;
                public T Value => default!;
            }
        }

        namespace Consumer
        {
            using Lumyte.Composition;

            [Composable]
            public partial class Box<T> where T : notnull
            {
                [ComposeParameter]
                public required T Value { get; init; }

                [ComposeContent]
                private IReadOnlyList<T> Children { get; set; } = [];
            }
        }
        """;

    [Fact]
    public void UnrelatedSourceChangeKeepsCompositionInputCached()
    {
        SyntaxTree component = CSharpSyntaxTree.ParseText(ComponentSource, path: "Box.cs");
        SyntaxTree unrelated = CSharpSyntaxTree.ParseText("internal class Unrelated { }", path: "Unrelated.cs");
        CSharpCompilation compilation = CreateCompilation(component, unrelated);
        var options = new GeneratorDriverOptions(
            IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: true);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CompositionGenerator().AsSourceGenerator()],
            driverOptions: options);
        driver = driver.RunGenerators(compilation);
        SyntaxTree changed = CSharpSyntaxTree.ParseText(
            "internal class Unrelated { public int Value => 1; }",
            path: "Unrelated.cs");
        compilation = compilation.ReplaceSyntaxTree(unrelated, changed);

        driver = driver.RunGenerators(compilation);

        GeneratorRunResult result = Assert.Single(driver.GetRunResult().Results);
        IncrementalGeneratorRunStep step = Assert.Single(result.TrackedSteps["CompositionInputs"]);
        var output = Assert.Single(step.Outputs);
        Assert.Equal(IncrementalStepRunReason.Cached, output.Reason);
    }

    [Fact]
    public void OmittingRequiredParameterProducesCompilerError()
    {
        SyntaxTree component = CSharpSyntaxTree.ParseText(ComponentSource, path: "Box.cs");
        SyntaxTree usage = CSharpSyntaxTree.ParseText(
            "internal static class Usage { public static object Create() => Consumer.Compose.Box<int>(); }",
            path: "Usage.cs");
        CSharpCompilation compilation = CreateCompilation(component, usage);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CompositionGenerator().AsSourceGenerator()]);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        Diagnostic error = Assert.Single(output.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Id == "CS7036"));
        Assert.Contains("value", error.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void GetOnlyComposeParameterProducesGeneratorError()
    {
        string source = ComponentSource.Replace(
            "public required T Value { get; init; }",
            "public T Value { get; } = default!;",
            StringComparison.Ordinal);
        CSharpCompilation compilation = CreateCompilation(
            CSharpSyntaxTree.ParseText(source, path: "Box.cs"));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CompositionGenerator().AsSourceGenerator()]);

        driver = driver.RunGenerators(compilation);

        Diagnostic error = Assert.Single(driver.GetRunResult().Diagnostics.Where(diagnostic =>
            diagnostic.Id == "LYC003"));
        Assert.Contains("Value", error.GetMessage(), StringComparison.Ordinal);
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] syntaxTrees)
    {
        string trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        ImmutableArray<MetadataReference> references = trustedAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
        return CSharpCompilation.Create(
            "IncrementalGeneratorConsumer",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
