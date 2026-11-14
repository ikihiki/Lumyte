using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lumyte.Composition.Generators.Benchmark;

/// <summary>Exercises the public incremental generator driver over independent consumers.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Core", "Composition.Generators", "SourceGeneration", "Screening", "Comparison", "Diagnostic")]
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
        MetadataReference[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(ComposableAttribute).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        SyntaxTree[] trees = Enumerable.Range(0, Count).Select(index => CSharpSyntaxTree.ParseText(Source(index, 1), path: $"consumer{index}.cs")).ToArray();
        _first = CSharpCompilation.Create("Consumer", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        _second = Change == "Target" && Count > 0 ? _first.ReplaceSyntaxTree(trees[0], CSharpSyntaxTree.ParseText(Source(0, 2), path: "consumer0.cs")) : Change == "Unrelated" ? _first.AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal class Unrelated { public int Value => 2; }")) : _first;
        _invalid = _first.AddSyntaxTrees(CSharpSyntaxTree.ParseText("using Lumyte.Composition; public static partial class Bad { public static partial class Definitions { [Composable] public partial class Invalid { [ComposeParameter] public static int Value { get; set; } } } }"));
        _warm = CreateDriver().RunGenerators(_first);
    }

    /// <summary>Creates a fresh driver and generates without final consumer compilation.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
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
    public GeneratorDriverRunResult DiagnoseInvalid() => CreateDriver().RunGenerators(_invalid).GetRunResult();

    /// <summary>Generates and evaluates final consumer compiler diagnostics separately.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
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

    private static GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(new CompositionGenerator().AsSourceGenerator());

    private static string Source(int index, int offset) => "using Lumyte.Composition; namespace Consumer; public static partial class Compose { public static partial class Definitions { [Composable] public static NodeINDEX MakeINDEX(int value = 7) => new LeafINDEX(); [ComposeFunctionAdapter(typeof(HostINDEX))] public abstract class NodeINDEX { public string? Key { get; set; } } public sealed class LeafINDEX : NodeINDEX { } public abstract class HostINDEX : NodeINDEX, ICompositionRebuildable { public System.Func<object>? Factory { get; private set; } void ICompositionRebuildable.SetCompositionFactory(System.Func<object> factory) => Factory = factory; protected abstract NodeINDEX Build(); } [Composable] public partial class ItemINDEX { [ComposeParameter] public int VALUE_MEMBER { get; set; } = 1; [ComposeContent] public int[] Children { get; set; } = []; [ComposeSlot] private static void Extra(ItemINDEX target, int[] children) => target.Children = children; [ComposeAction] private static void Set(ItemINDEX target, int value) => target.VALUE_MEMBER = value; } } }".Replace("INDEX", index.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("VALUE_MEMBER", offset == 1 ? "Value" : "UpdatedValue", StringComparison.Ordinal);
}
