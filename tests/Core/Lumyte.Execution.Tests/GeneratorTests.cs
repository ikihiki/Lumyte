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
            public readonly partial struct State(object original)
            {
                private readonly object _original = original;
                [ExecutionCommand] private Target CreateCore(int clip) => new();
                [ExecutionCommand] private void PlayCore(Target voice, float volume) { }
                [ExecutionCommand] private void DestroyCore(Target voice) { }
                [ExecutionCommand(Input = ExecutionInputKind.ExistingCommand)] private int ProbeCore(in int input, in WorkContext work) => input;
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
        Assert.DoesNotContain("struct Voice", generated, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask", generated, StringComparison.Ordinal);
        Assert.Contains("static readonly global::Lumyte.Execution.CommandPort", generated, StringComparison.Ordinal);
        Assert.Contains(".Enqueue(in __command", generated, StringComparison.Ordinal);
        Assert.Contains("context.Enqueue(Key", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(".ExecuteAsync(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTraceHidden", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("async ", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("VoiceBinding", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Reflection", generated, StringComparison.Ordinal);
        Assert.Contains("ProbeCommand", generated, StringComparison.Ordinal);
    }

    /// <summary>Wrapper receiver declarations register stateless handlers and invoke the actual target.</summary>
    [Fact]
    public void WrapperReceiversCompileWithoutRegistrationInstances()
    {
        const string ReceiverSource = "using Lumyte.Execution; namespace Example; public readonly partial struct Voice(object original) { private readonly object _original = original; [ExecutionCommand] private void PlayCore(int gain) {} }";
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(ReceiverSource);
        Assert.Empty(result.Run.Diagnostics);
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.Run.GeneratedTrees).ToString();
        Assert.Contains("static PlayWork DeclarePlay", generated, StringComparison.Ordinal);
        Assert.Contains("private readonly struct __LumyteExecutionPlayHandler", generated, StringComparison.Ordinal);
        Assert.Contains("command.__Target.@PlayCore(command.F0)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("new(this)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("state.", generated, StringComparison.Ordinal);
    }

    /// <summary>Invalid forms receive deterministic errors without partially generated classes.</summary>
    /// <param name="declaration">The invalid container or method.</param>
    /// <param name="id">The required diagnostic.</param>
    [Theory]
    [InlineData("public partial class State { [ExecutionCommand] private void GoCore() {} }", "LMEXEC001")]
    [InlineData("public partial struct State { [ExecutionCommand] private void GoCore() {} }", "LMEXEC001")]
    [InlineData("public readonly partial struct State { private readonly int _original; [ExecutionCommand] private void GoCore() {} }", "LMEXEC007")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand] private async System.Threading.Tasks.Task GoCore() { await System.Threading.Tasks.Task.Yield(); } }", "LMEXEC002")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand] private void GoCore(ref int x) {} }", "LMEXEC005")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand] private void GoCore(System.Action action) {} }", "LMEXEC003")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand] private void GoCore(System.Span<int> data) {} }", "LMEXEC003")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand(Name = \"\")] private void GoCore() {} }", "LMEXEC004")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand] [ExecutionUpdate] private void GoCore() {} }", "LMEXEC004")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand] private System.Threading.Tasks.Task GoCore() => System.Threading.Tasks.Task.CompletedTask; }", "LMEXEC006")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand(Input = ExecutionInputKind.ExistingCommand)] private void GoCore(in int? x) {} }", "LMEXEC005")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; private sealed class __LumyteExecutionCollision {} [ExecutionCommand] private void GoCore() {} }", "LMEXEC001")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand] private void GoAsync() {} }", "LMEXEC001")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; private readonly object _other = original; [ExecutionCommand] private void GoCore() {} }", "LMEXEC007")]
    [InlineData("public partial class State { [ExecutionRange(\"Length\")] private void Go(in int x, int a, int b) {} private int Length(in int x) => x; private int Length(int x) => x; }", "LMEXEC005")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand] private void GoCore() { System.GC.KeepAlive(original); } }", "LMEXEC007")]
    [InlineData("public readonly partial struct State(object original) { private readonly object _original = original; [ExecutionCommand] private void GoCore(int receiver) {} }", "LMEXEC003")]
    public void InvalidDeclarationsFailWholeContainer(string declaration, string id)
    {
        (GeneratorDriverRunResult Run, Compilation Output) result = Run("#nullable enable\nusing Lumyte.Execution; namespace Example; " + declaration + " public sealed class Target {}");
        Assert.Contains(result.Run.Diagnostics, diagnostic => diagnostic.Id == id && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.Run.GeneratedTrees);
    }

    /// <summary>Only the direct wrapper API is available; legacy switches and targets are removed.</summary>
    [Fact]
    public void LegacySenderModesAreRemoved()
    {
        Assert.Null(typeof(ExecutionCommandAttribute).GetProperty("Sender"));
        Assert.Null(typeof(ExecutionCommandAttribute).GetProperty("UseReceiver"));
        Assert.Null(typeof(ExecutionRuntime).Assembly.GetType("Lumyte.Execution.ExecutionTargetAttribute"));
    }

    /// <summary>Ordering and hints do not depend on source path or method source order.</summary>
    [Fact]
    public void GeneratedOutputIsDeterministic()
    {
        const string A = "[ExecutionCommand] private void ZCore(int value) {}";
        const string B = "[ExecutionCommand] private void ACore(int value) {}";
        string first = Assert.Single(Run("using Lumyte.Execution; namespace Example; public readonly partial struct State(object original) { private readonly object _original = original; " + A + B + " }", "/first/state.cs").Run.GeneratedTrees).ToString();
        string second = Assert.Single(Run("using Lumyte.Execution; namespace Example; public readonly partial struct State(object original) { private readonly object _original = original; " + B + A + " }", "/other/state.cs").Run.GeneratedTrees).ToString();
        Assert.Equal(first, second);
    }

    /// <summary>Explicit pending operation results stay wrapped and require startup authority.</summary>
    [Fact]
    public void PendingOperationResultIsNotAwaitedByHandler()
    {
        const string Source = """
            using Lumyte.Execution;
            public readonly partial struct State(object original)
            {
                private readonly object _original = original;
                [ExecutionCommand(AllowAwaitableResult = true)]
                private System.Threading.Tasks.Task InitializeCore() => System.Threading.Tasks.Task.CompletedTask;
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

    /// <summary>Async senders retain work helper names and compile at call sites.</summary>
    [Fact]
    public void AsyncSendersExposeOnlyCanonicalNames()
    {
        const string Source = """
            using Lumyte.Execution;
            public readonly partial struct Voice(object original)
            {
                private readonly object _original = original;
                [ExecutionCommand] private int PlayCore(int gain) => gain;
                [ExecutionCommand(Name = "StopAsync")] private void StopCore() { }
            }
            public static class Caller
            {
                public static async System.Threading.Tasks.Task Use(Voice voice, ModuleBuilder builder, WorkOptions options, ModuleContext context, CommandBatch batch)
                {
                    Voice.PlayWork work = Voice.DeclarePlay(builder, options);
                    await voice.PlayAsync(1);
                    await voice.PlayAsync(1, cancellationToken: default);
                    await work.ExecuteAsync(context, voice, 1);
                    voice.AddPlayTo(batch, 1);
                    await voice.StopAsync();
                }
            }
            """;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(Source);
        Assert.Empty(result.Run.Diagnostics);
        Assert.DoesNotContain(result.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.Run.GeneratedTrees).ToString();
        Assert.Contains("@PlayAsync(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("@Play(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("StopAsyncAsync", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(".ExecuteAsync(", generated, StringComparison.Ordinal);
    }

    /// <summary>Canonical sender names reject collisions with user or generated members.</summary>
    /// <param name="collision">A preexisting member occupying the alias name.</param>
    [Theory]
    [InlineData("public void PlayAsync() { }")]
    [InlineData("[ExecutionCommand(Name = \"PlayAsync\")] private void OtherCore() { }")]
    public void AsyncSenderCollisionsAreReported(string collision)
    {
        string source = "using Lumyte.Execution; public readonly partial struct Voice(object original) { private readonly object _original = original; [ExecutionCommand] private void PlayCore() { } " + collision + " }";
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(source);
        Assert.Contains(result.Run.Diagnostics, diagnostic => diagnostic.Id == "LMEXEC001");
        Assert.Empty(result.Run.GeneratedTrees);
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
