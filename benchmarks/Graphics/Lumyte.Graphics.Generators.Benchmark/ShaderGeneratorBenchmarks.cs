using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lumyte.Graphics.Generators.Benchmark;

/// <summary>Actual Roslyn generation with fresh, warm and semantically changed declarations.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ShaderGeneratorBenchmarks
{
    private CSharpCompilation _baseline = null!;
    private CSharpCompilation _changed = null!;
    private GeneratorDriver _warm = null!;

    /// <summary>Gets or sets the bounded declaration profile.</summary>
    [Params(1, 16)]
    public int Declarations { get; set; }

    /// <summary>Builds valid consumers and primes the retained incremental driver.</summary>
    [GlobalSetup]
    public void Setup()
    {
        string[] paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Runtime reference set unavailable.")).Split(Path.PathSeparator);
        MetadataReference[] references = paths.Append(typeof(IShaderArguments).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        _baseline = CSharpCompilation.Create("GeneratedConsumer", [CSharpSyntaxTree.ParseText(Source(false))], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        _changed = _baseline.RemoveAllSyntaxTrees().AddSyntaxTrees(CSharpSyntaxTree.ParseText(Source(true)));
        _warm = NewDriver().RunGenerators(_baseline);
    }

    /// <summary>Creates a fresh incremental driver and generates every declaration.</summary>
    /// <returns>The deterministic generated source fingerprint.</returns>
    [Benchmark]
    public string FreshGeneration() => Fingerprint(NewDriver().RunGenerators(_baseline));

    /// <summary>Reuses the primed driver against the identical immutable compilation.</summary>
    /// <returns>The generated source fingerprint.</returns>
    [Benchmark]
    public string IncrementalWarm() => Fingerprint(_warm.RunGenerators(_baseline));

    /// <summary>Reuses baseline state but changes the actual semantic field type.</summary>
    /// <returns>The changed generated source fingerprint.</returns>
    [Benchmark]
    public string SemanticDeclarationChange() => Fingerprint(_warm.RunGenerators(_changed));

    /// <summary>Builds the changed declaration from scratch for the incremental correctness oracle.</summary>
    /// <returns>The independently generated changed fingerprint.</returns>
    public string FreshChangedGeneration() => Fingerprint(NewDriver().RunGenerators(_changed));

    /// <summary>Emits a real generated consumer, detecting invalid generated APIs.</summary>
    /// <param name="changed">Whether to emit the semantic field-type edit.</param>
    /// <returns>The emitted assembly byte count.</returns>
    public int EmitConsumer(bool changed = false)
    {
        NewDriver().RunGeneratorsAndUpdateCompilation(changed ? _changed : _baseline, out Compilation output, out _);
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = output.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return checked((int)stream.Length);
    }

    private static GeneratorDriver NewDriver() => CSharpGeneratorDriver.Create(new ShaderArgumentsGenerator().AsSourceGenerator());

    private static string Fingerprint(GeneratorDriver driver)
    {
        GeneratorDriverRunResult result = driver.GetRunResult();
        if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) || result.GeneratedTrees.Length == 0)
        {
            throw new InvalidOperationException("Generator did not produce valid output.");
        }

        string source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private string Source(bool changed)
    {
        string fieldType = changed ? "System.Numerics.Vector4" : "System.Numerics.Vector3";
        return "using Lumyte.Graphics.Abstractions; namespace Consumer; " + string.Join(" ", Enumerable.Range(0, Declarations).Select(index => $"public partial struct Arguments{index} : IShaderArguments {{ public {fieldType} Position; public System.Numerics.Matrix4x4 Transform; }}"));
    }
}
