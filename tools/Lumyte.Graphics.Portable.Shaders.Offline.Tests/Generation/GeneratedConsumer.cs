using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Lumyte.Graphics.Portable.Shaders.Offline.Tests;

internal static class GeneratedConsumer
{
    internal static Assembly Compile(PortableShaderBuildResult result, string consumer)
    {
        var trees = result.GeneratedSources.Select(file => CSharpSyntaxTree.ParseText(file.Content, path: file.FileName))
            .Append(CSharpSyntaxTree.ParseText(consumer));
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = paths.Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("PortableGeneratedConsumer" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new Resources.Generators.ResourceInputGenerator().AsSourceGenerator()],
            result.ResourceInputs.Select(file => (AdditionalText)new TextFile(file.FileName, file.Content)));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error));
        using var bytes = new MemoryStream();
        var emission = output.Emit(bytes);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(bytes.ToArray());
    }

    private sealed class TextFile(string path, string value) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(value);
    }
}
