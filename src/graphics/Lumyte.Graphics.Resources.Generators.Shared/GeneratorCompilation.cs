using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Lumyte.Graphics.Resources.Generators.Tests;

internal static class GeneratorCompilation
{
    internal static (Compilation Output, GeneratorDriverRunResult Result) Generate(
        IIncrementalGenerator generator, string source, params (string Path, string Text)[] schemas)
    {
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = paths.Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ManagedInputConsumer",
            [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator.AsSourceGenerator()],
            schemas.Select(schema => (AdditionalText)new TextFile(schema.Path, schema.Text)));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (output, driver.GetRunResult());
    }

    internal static Assembly Compile(IIncrementalGenerator generator, string source, params (string Path, string Text)[] schemas)
    {
        var (output, result) = Generate(generator, source, schemas);
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        using var bytes = new MemoryStream();
        var emission = output.Emit(bytes);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(bytes.ToArray());
    }

    private sealed class TextFile(string path, string text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
