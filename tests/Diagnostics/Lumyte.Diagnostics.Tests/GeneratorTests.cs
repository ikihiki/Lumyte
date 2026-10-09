using System.Runtime.Loader;
using Lumyte.Diagnostics.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Lumyte.Diagnostics.Tests;

/// <summary>Checks actual generator diagnostics and emitted compilations.</summary>
public sealed class GeneratorTests
{
    /// <summary>Checks minimal attributes, optional context and inferred names.</summary>
    [Fact]
    public void MinimalDeclarationCompilesAndInfersNames()
    {
        const string Source = """
            using Lumyte.Diagnostics;
            namespace Demo;
            public sealed partial class Adapter
            {
                [DiagnosticOperation(DiagnosticPermission.Observe)]
                private DiagnosticResult<Receipt> GetURLValue(string userID)
                    => DiagnosticResult<Receipt>.Success(new(userID));
            }
            public sealed record Receipt(string URLValue);
            """;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(Source);
        Assert.Empty(result.Run.Diagnostics);
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.Run.GeneratedTrees).ToString();
        Assert.Contains("get-url-value", generated, StringComparison.Ordinal);
        Assert.Contains("user-id", generated, StringComparison.Ordinal);
        Assert.Contains("url-value", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Reflection", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticValue.From", generated, StringComparison.Ordinal);
        Assert.Contains("WriteTo<TWriter>", generated, StringComparison.Ordinal);
    }

    /// <summary>Checks invalid signatures and accidental wire-name collisions are compile errors.</summary>
    /// <param name="declaration">The invalid declaration.</param>
    /// <param name="code">The expected diagnostic.</param>
    [Theory]
    [InlineData("public class Adapter { [DiagnosticOperation] private DiagnosticResult<R> Go() => null!; }", "LMDIAG001")]
    [InlineData("public partial class Adapter { [DiagnosticOperation] private async System.Threading.Tasks.Task<DiagnosticResult<R>> Go() => null!; }", "LMDIAG002")]
    [InlineData("public partial class Adapter { [DiagnosticOperation] private DiagnosticResult<R> Go(int x) => null!; }", "LMDIAG004")]
    [InlineData("public partial class Adapter { [DiagnosticOperation] private DiagnosticResult<R> Go([DiagnosticArgument(Minimum = 2, Maximum = 1)] long x) => null!; }", "LMDIAG005")]
    [InlineData("public partial class Adapter { [DiagnosticOperation] private DiagnosticResult<R> GetURL() => null!; [DiagnosticOperation] private DiagnosticResult<R> GetUrl() => null!; }", "LMDIAG003")]
    public void InvalidDeclarationsAreRejected(string declaration, string code)
    {
        (GeneratorDriverRunResult Run, Compilation Output) result = Run("using Lumyte.Diagnostics; namespace Demo; " + declaration + " public sealed record R(bool Ok);");
        Assert.Contains(result.Run.Diagnostics, diagnostic => diagnostic.Id == code && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.Run.GeneratedTrees);
    }

    /// <summary>Checks explicit stable IDs, revision requirements and ignored output members.</summary>
    [Fact]
    public void OptionalAttributesOverrideInference()
    {
        const string Source = """
            using Lumyte.Diagnostics;
            namespace Demo;
            public sealed partial class Adapter
            {
                [DiagnosticOperation(Id = "stable", RequiresRevision = true)]
                private DiagnosticResult<Receipt> Go([DiagnosticArgument("amount", Minimum = 1, Maximum = 5)] long count)
                    => DiagnosticResult<Receipt>.Success(new(count, new object()));
            }
            public sealed record Receipt(
                [property: DiagnosticMember("result")] long Count,
                [property: DiagnosticIgnore] object Internal);
            """;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(Source);
        Assert.Empty(result.Run.Diagnostics);
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.Run.GeneratedTrees).ToString();
        Assert.Contains("\"stable\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"amount\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"result\"", generated, StringComparison.Ordinal);
        Assert.Contains("RequiresRevision: true", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Internal", generated, StringComparison.Ordinal);
    }

    /// <summary>Checks generated snapshots copy mutable domain properties before background serialization.</summary>
    [Fact]
    public void GeneratedOutputSnapshotsMutableProperties()
    {
        const string Source = """
            using Lumyte.Diagnostics;
            namespace Demo;
            public sealed partial class Adapter
            {
                private readonly Receipt receipt = new();
                [DiagnosticOperation(DiagnosticPermission.Observe)]
                private DiagnosticResult<Receipt> Read() => DiagnosticResult<Receipt>.Success(receipt);
                public void Change() => receipt.Text = "after";
            }
            public sealed class Receipt
            {
                public string Text { get; set; } = "before";
            }
            """;
        (GeneratorDriverRunResult Run, Compilation Output) generated = Run(Source);
        using var stream = new MemoryStream();
        Assert.True(generated.Output.Emit(stream).Success);
        stream.Position = 0;
        var context = new AssemblyLoadContext("snapshot-probe", isCollectible: true);
        try
        {
            Type type = context.LoadFromStream(stream).GetType("Demo.Adapter")!;
            var adapter = (IDiagnosticContributor)Activator.CreateInstance(type)!;
            var operations = new DiagnosticOperationSet(adapter);
            DiagnosticOperationResult result = operations.Invoke("read", new Dictionary<string, DiagnosticValue>(), new(Guid.NewGuid(), Guid.NewGuid(), default, "actor", null, default), new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe });
            type.GetMethod("Change")!.Invoke(adapter, null);
            DiagnosticOutputValues output = Assert.IsAssignableFrom<DiagnosticOutputValues>(result.Values);
            StringWriter writer = default;
            output.WriteTo(ref writer);
            Assert.Equal("before", writer.Value);
        }
        finally
        {
            context.Unload();
        }
    }

    private static (GeneratorDriverRunResult Run, Compilation Output) Run(string source)
    {
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = paths.Append(typeof(DiagnosticOperationAttribute).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "GeneratorProbe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OperationGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (driver.GetRunResult(), output);
    }

    private struct StringWriter : IDiagnosticValueWriter
    {
        public string? Value { get; private set; }

        public void Write(string name, bool value) => throw new NotSupportedException();

        public void Write(string name, long value) => throw new NotSupportedException();

        public void Write(string name, double value) => throw new NotSupportedException();

        public void Write(string name, string value) => Value = value;
    }
}
