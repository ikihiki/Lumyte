using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lumyte.Execution.Generators.Benchmark;

/// <summary>Exercises the public incremental generator driver over independent consumers.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Core", "Execution.Generators", "SourceGeneration")]
public class GeneratorBenchmarks
{
    private CSharpCompilation _first = null!;
    private CSharpCompilation _second = null!;
    private CSharpCompilation _invalid = null!;
    private GeneratorDriver _warm = null!;
    private bool _alternate;

    /// <summary>Gets or sets the finite declaration count.</summary>
    [Params(0, 1, 8, 32)]
    public int Count { get; set; } = 8;

    /// <summary>Gets or sets the warm rerun change kind.</summary>
    public string Change { get; set; } = "Unchanged";

    /// <summary>Parses fixed inputs and primes only the warm driver.</summary>
    [GlobalSetup]
    public void Setup()
    {
        MetadataReference[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(ExecutionCommandAttribute).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        SyntaxTree[] trees = Enumerable.Range(0, Count).Select(index => CSharpSyntaxTree.ParseText(Source(index, 1), path: $"consumer{index}.cs")).ToArray();
        _first = CSharpCompilation.Create("Consumer", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        _second = Change == "Target" && Count > 0 ? _first.ReplaceSyntaxTree(trees[0], CSharpSyntaxTree.ParseText(Source(0, 2), path: "consumer0.cs")) : Change == "Unrelated" ? _first.AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal class Unrelated { public int Value => 2; }")) : _first;
        _invalid = _first.AddSyntaxTrees(CSharpSyntaxTree.ParseText("using Lumyte.Execution; public partial class Invalid { [ExecutionCommand] private void GoCore() {} }"));
        _warm = CreateDriver().RunGenerators(_first);
    }

    /// <summary>Creates a fresh driver and generates without final consumer compilation.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-GEN-01")]
    public GeneratorDriverRunResult Cold() => CreateDriver().RunGenerators(_first).GetRunResult();

    /// <summary>Retains only the latest driver while alternating two fixed inputs.</summary>
    /// <returns>The actual operation result.</returns>
    public GeneratorDriverRunResult Warm()
    {
        _alternate = !_alternate;
        _warm = _warm.RunGenerators(_alternate ? _second : _first);
        return _warm.GetRunResult();
    }

    /// <summary>Runs malformed declarations through actual generator diagnostics.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-GEN-03")]
    public GeneratorDriverRunResult DiagnoseInvalid() => CreateDriver().RunGenerators(_invalid).GetRunResult();

    /// <summary>Generates and evaluates final consumer compiler diagnostics separately.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-GEN-01")]
    public int ConsumerCompile()
    {
        CreateDriver().RunGeneratorsAndUpdateCompilation(_first, out Compilation output, out _);
        return output.GetDiagnostics().Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Checks that target declaration edits actually invalidate generated output.</summary>
    /// <returns>Whether the two fresh generated-source fingerprints differ.</returns>
    public bool TargetOutputChanges() => FreshFingerprint(false) != FreshFingerprint(true);

    /// <summary>Compiles the edited consumer independently of the timed driver cases.</summary>
    /// <returns>The edited consumer compiler error count.</returns>
    public int ChangedConsumerCompile()
    {
        CreateDriver().RunGeneratorsAndUpdateCompilation(_second, out Compilation output, out _);
        return output.GetDiagnostics().Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    internal static string Fingerprint(GeneratorDriverRunResult result) => string.Join("\n", result.Results.SelectMany(item => item.GeneratedSources).OrderBy(item => item.HintName).Select(item => item.HintName + item.SourceText.ToString()));

    internal string FreshFingerprint(bool second) => Fingerprint(CreateDriver().RunGenerators(second ? _second : _first).GetRunResult());

    private static GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(new ExecutionGenerator().AsSourceGenerator());

    private static string Source(int index, int offset) => "using Lumyte.Execution; namespace Consumer; public readonly partial struct StateINDEX(object original) { private readonly object _original = original; [ExecutionCommand] private PAYLOAD ProbeCore(PAYLOAD value) => value; [ExecutionUpdate] private void Tick(in UpdateTick tick, in WorkContext work) { } [ExecutionRange(nameof(Length))] private void Fill(in int value, int first, int endExclusive) { } private int Length(in int value) => value; }".Replace("INDEX", index.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("PAYLOAD", offset == 1 ? "int" : "long", StringComparison.Ordinal);
}
