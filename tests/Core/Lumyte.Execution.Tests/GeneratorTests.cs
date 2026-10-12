using Lumyte.Execution.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Compiles emitted sender code and verifies diagnostic and generation contracts.</summary>
public sealed class GeneratorTests
{
    /// <summary>Commands, existing inputs, updates, ranges, and grouped targets compile together.</summary>
    [Fact]
    public void GeneratedWorkKindsCompileAndSenderBodiesAreExpanded()
    {
        const string Source = """
            using Lumyte.Execution;
            namespace Example;
            public sealed partial class State
            {
                [ExecutionCommand] private Target Create(int clip) => new();
                [ExecutionCommand(Sender = "Voice")] private void Play([ExecutionTarget] Target voice, float volume) { }
                [ExecutionCommand(Sender = "Voice")] private void Destroy([ExecutionTarget] Target voice) { }
                [ExecutionCommand(Input = ExecutionInputKind.ExistingCommand)] private int Probe(in int input, in WorkContext work) => input;
                [ExecutionUpdate] private void Tick(in UpdateTick tick, in WorkContext work) { }
                [ExecutionRange(nameof(Length))] private void Fill(in int context, int first, int endExclusive) { }
                private int Length(in int input) => input;
            }
            public sealed class Target { }
            """;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(Source);
        Assert.Empty(result.Run.Diagnostics);
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.Run.GeneratedTrees).ToString();
        Assert.Contains("public readonly struct Voice", generated, StringComparison.Ordinal);
        Assert.Contains("static readonly global::Lumyte.Execution.CommandPort", generated, StringComparison.Ordinal);
        Assert.Contains(".TryEnqueue(in __command", generated, StringComparison.Ordinal);
        Assert.Contains("context.TryEnqueue(Key", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(".ExecuteAsync(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTraceHidden", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("async ", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("VoiceBinding", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Reflection", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("ProbeCommand", generated, StringComparison.Ordinal);
    }

    /// <summary>Wrapper receiver declarations register stateless handlers and invoke the actual target.</summary>
    [Fact]
    public void WrapperReceiversCompileWithoutRegistrationInstances()
    {
        const string ReceiverSource = "using Lumyte.Execution; namespace Example; public partial class Voice { [ExecutionCommand(Sender = \"Commands\", UseReceiver = true)] private void Play(int gain) {} }";
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(ReceiverSource);
        Assert.Empty(result.Run.Diagnostics);
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.Run.GeneratedTrees).ToString();
        Assert.Contains("static CommandsWork DeclareCommands", generated, StringComparison.Ordinal);
        Assert.Contains("private readonly struct __LumyteExecutionPlayHandler", generated, StringComparison.Ordinal);
        Assert.Contains("command.__Target.@Play(command.F0)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("new(this)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("state.", generated, StringComparison.Ordinal);
    }

    /// <summary>Invalid forms receive deterministic errors without partially generated classes.</summary>
    /// <param name="declaration">The invalid container or method.</param>
    /// <param name="id">The required diagnostic.</param>
    [Theory]
    [InlineData("public class State { [ExecutionCommand] private void Go() {} }", "LMEXEC001")]
    [InlineData("public partial class State { [ExecutionCommand] private async System.Threading.Tasks.Task Go() { await System.Threading.Tasks.Task.Yield(); } }", "LMEXEC002")]
    [InlineData("public partial class State { [ExecutionCommand] private void Go(ref int x) {} }", "LMEXEC005")]
    [InlineData("public partial class State { [ExecutionCommand] private void Go(System.Action action) {} }", "LMEXEC003")]
    [InlineData("public partial class State { [ExecutionCommand] private void Go(System.Span<int> data) {} }", "LMEXEC003")]
    [InlineData("public partial class State { [ExecutionCommand(Name = \"\")] private void Go() {} }", "LMEXEC004")]
    [InlineData("public partial class State { [ExecutionCommand] [ExecutionUpdate] private void Go() {} }", "LMEXEC004")]
    [InlineData("public partial class State { [ExecutionCommand] private System.Threading.Tasks.Task Go() => System.Threading.Tasks.Task.CompletedTask; }", "LMEXEC006")]
    [InlineData("public partial class State { [ExecutionCommand(Sender = \"Voice\")] private void Play(Target x) {} }", "LMEXEC007")]
    [InlineData("public partial class State { [ExecutionCommand(Sender = \"Voice\")] private void Play([ExecutionTarget] Target? x) {} }", "LMEXEC007")]
    [InlineData("public partial class State { [ExecutionCommand(Input = ExecutionInputKind.ExistingCommand)] private void Go(in int? x) {} }", "LMEXEC005")]
    [InlineData("public partial class State { [ExecutionCommand(Sender = \"Voice\")] private void Voice([ExecutionTarget] Target x) {} }", "LMEXEC007")]
    [InlineData("public partial class State { private sealed class __LumyteExecutionCollision {} [ExecutionCommand] private void Go() {} }", "LMEXEC001")]
    [InlineData("public partial class State { [ExecutionRange(\"Length\")] private void Go(in int x, int a, int b) {} private int Length(in int x) => x; private int Length(int x) => x; }", "LMEXEC005")]
    [InlineData("public partial class State { [ExecutionCommand(UseReceiver = true)] private void Play() {} }", "LMEXEC007")]
    [InlineData("public partial class State { [ExecutionCommand(Sender = \"Voice\", UseReceiver = true)] private void Play([ExecutionTarget] Target x) {} }", "LMEXEC007")]
    [InlineData("public partial class State { [ExecutionCommand(Sender = \"Voice\", UseReceiver = true, Input = ExecutionInputKind.ExistingCommand)] private void Play(in int x) {} }", "LMEXEC007")]
    public void InvalidDeclarationsFailWholeContainer(string declaration, string id)
    {
        (GeneratorDriverRunResult Run, Compilation Output) result = Run("#nullable enable\nusing Lumyte.Execution; namespace Example; " + declaration + " public sealed class Target {}");
        Assert.Contains(result.Run.Diagnostics, diagnostic => diagnostic.Id == id && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.Run.GeneratedTrees);
    }

    /// <summary>Ordering and hints do not depend on source path or method source order.</summary>
    [Fact]
    public void GeneratedOutputIsDeterministic()
    {
        const string A = "[ExecutionCommand] private void Z(int value) {}";
        const string B = "[ExecutionCommand] private void A(int value) {}";
        string first = Assert.Single(Run("using Lumyte.Execution; namespace Example; public partial class State { " + A + B + " }", "/first/state.cs").Run.GeneratedTrees).ToString();
        string second = Assert.Single(Run("using Lumyte.Execution; namespace Example; public partial class State { " + B + A + " }", "/other/state.cs").Run.GeneratedTrees).ToString();
        Assert.Equal(first, second);
    }

    /// <summary>Explicit pending operation results stay wrapped and require startup authority.</summary>
    [Fact]
    public void PendingOperationResultIsNotAwaitedByHandler()
    {
        const string Source = """
            using Lumyte.Execution;
            public partial class State
            {
                [ExecutionCommand(AllowAwaitableResult = true)]
                private System.Threading.Tasks.Task Initialize() => System.Threading.Tasks.Task.CompletedTask;
            }
            """;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(Source);
        Assert.Empty(result.Run.Diagnostics);
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.Run.GeneratedTrees).ToString();
        Assert.Contains("WorkAdmission.Startup", generated, StringComparison.Ordinal);
        Assert.Contains("ValueTask<global::System.Threading.Tasks.Task>", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("await ", generated, StringComparison.Ordinal);
    }

    private static (GeneratorDriverRunResult Run, Compilation Output) Run(string source, string path = "/state.cs")
    {
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = paths.Append(typeof(ExecutionRuntime).Assembly.Location).Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path));
        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create("ExecutionGeneratorProbe", [CSharpSyntaxTree.ParseText(source, parse, path)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ExecutionGenerator().AsSourceGenerator()], parseOptions: parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (driver.GetRunResult(), output);
    }
}
