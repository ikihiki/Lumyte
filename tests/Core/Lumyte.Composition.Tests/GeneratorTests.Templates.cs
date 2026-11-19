using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Lumyte.Composition.Tests;

/// <summary>Checks typed template slots in independent consumers.</summary>
public sealed partial class GeneratorTests
{
    private const string TemplateHost = "using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { [Composable] public partial class Item { ";

    /// <summary>Checks every supported arity uses the exact closed template and supports constants.</summary>
    [Fact]
    public void TemplateSlotsCompileAtEveryArity()
    {
        for (int arity = 1; arity <= 16; arity++)
        {
            string types = string.Join(", ", Enumerable.Repeat("int", arity).Append("string"));
            string args = string.Join(", ", Enumerable.Range(1, arity).Select(index => "a" + index));
            string source = TemplateHost + "[ComposeSlot] private static void Row(Item target, CompositionTemplate<" + types + "> template) { } } } } public class Usage { public object Build() => Compose.Item()[Compose.Item.Row()[(" + args + ") => a1.ToString()], Compose.Item.Row()[string.Empty]]; }";
            GeneratorDriverRunResult result = Run(source, out Compilation output);
            Assert.Empty(result.Diagnostics);
            Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
            Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("CompositionTemplateSlot<", StringComparison.Ordinal));
        }
    }

    /// <summary>Checks aliases, constrained generics, mixed children and private slot methods execute without evaluation.</summary>
    [Fact]
    public void TemplateSlotsPreserveInferenceIdentityAndEvaluationBoundaries()
    {
        const string Source = """
            using Lumyte.Composition;
            using Alias = Lumyte.Composition.CompositionTemplate<int, string>;
            public sealed record Person(string Name);
            public static partial class Compose
            {
                public static partial class Definitions
                {
                    [Composable] public partial class ListView<T> where T : notnull
                    {
                        [ComposeContent] public T[] Items { get; set; } = [];
                        public CompositionTemplate<T, ListView<T>, string>? Template { get; private set; }
                        public int[] HeaderItems { get; private set; } = [];
                        [ComposeSlot] private static void Item(ListView<T> target, CompositionTemplate<T, ListView<T>, string> template) => target.Template = template;
                        [ComposeSlot] private static void Header(ListView<T> target, int[] children) => target.HeaderItems = children;
                    }
                    [Composable] public partial class Callback
                    {
                        [ComposeParameter] public Alias? Optional { get; init; } = _ => "default";
                        [ComposeParameter] public required Alias Required { get; init; }
                    }
                }
            }
            public static class Usage
            {
                public static bool Check()
                {
                    int calls = 0;
                    var row = Compose.ListViewFactory<Person>().Item()[(person, list) => { calls++; return person.Name + list.Items.Length; }];
                    var ada = new Person("Ada");
                    var first = Compose.ListView<Person>()[ada, row, Compose.ListViewFactory<Person>().Header()[7]];
                    var second = Compose.ListView<Person>()[ada, ada, row];
                    if (calls != 0 || !object.ReferenceEquals(first.Template, second.Template)) return false;
                    if (first.Template!(ada, first) != "Ada1" || second.Template!(ada, second) != "Ada2" || calls != 2 || first.HeaderItems[0] != 7) return false;
                    int builds = 0;
                    string Build() { builds++; return new string('x', 1); }
                    var constant = Compose.ListViewFactory<Person>().Item()[Build()];
                    var a = Compose.ListView<Person>()[constant];
                    var b = Compose.ListView<Person>()[constant];
                    if (builds != 1 || !object.ReferenceEquals(a.Template!(ada, a), b.Template!(ada, b))) return false;
                    Alias callback = value => value.ToString();
                    var configured = Compose.Callback(required: callback, optional: callback);
                    var omitted = Compose.Callback(required: callback);
                    return object.ReferenceEquals(configured.Required, callback) && object.ReferenceEquals(configured.Optional, callback) && omitted.Optional is null;
                }
            }
            """;
        Assert.True(ExecuteTemplateConsumer(Source));
    }

    /// <summary>Checks slot-only Item names, method groups, local functions and tuple input.</summary>
    [Fact]
    public void TemplateSlotsAcceptMethodGroupsAndTupleInput()
    {
        const string Source = """
            using Lumyte.Composition;
            public static partial class Compose
            {
                public static partial class Definitions
                {
                    [Composable] public partial class Holder
                    {
                        public CompositionTemplate<(int Number, string Text), string>? Template { get; private set; }
                        [ComposeSlot] private static void Item(Holder target, CompositionTemplate<(int Number, string Text), string> template) => target.Template = template;
                    }
                }
            }
            public static class Usage
            {
                private static string Format((int Number, string Text) value) => value.Text + value.Number;
                public static bool Check()
                {
                    var first = Compose.Holder()[Compose.Holder.Item()[Format]];
                    string Local((int Number, string Text) value) => Format(value);
                    var second = Compose.Holder()[Compose.Holder.Item()[Local]];
                    return first.Template!((3, "a")) == "a3" && second.Template!((4, "b")) == "b4";
                }
            }
            """;
        Assert.True(ExecuteTemplateConsumer(Source));
    }

    /// <summary>Checks malformed template slot declarations retain deliberate diagnostics.</summary>
    /// <param name="declaration">The malformed slot.</param>
    [Theory]
    [InlineData("[ComposeSlot] private void Row(Item target, CompositionTemplate<int, string> template) { }")]
    [InlineData("[ComposeSlot] private static void Row(object target, CompositionTemplate<int, string> template) { }")]
    [InlineData("[ComposeSlot] private static int Row(Item target, CompositionTemplate<int, string> template) => 0;")]
    [InlineData("[ComposeSlot] private static async void Row(Item target, CompositionTemplate<int, string> template) { await System.Threading.Tasks.Task.Yield(); }")]
    [InlineData("[ComposeSlot] private static void Row<T>(Item target, CompositionTemplate<T, string> template) { }")]
    [InlineData("[ComposeSlot] private static void Row(Item target, ref CompositionTemplate<int, string> template) { }")]
    [InlineData("[ComposeSlot] private static void Row(Item target, in CompositionTemplate<int, string> template) { }")]
    [InlineData("[ComposeSlot] private static void Row(Item target, CompositionTemplate<int, string>? template = null) { }")]
    [InlineData("[ComposeSlot] private static void Row(Item target, params CompositionTemplate<int, string>[] template) { }")]
    [InlineData("[ComposeSlot] private static void Row(Item target, System.Func<int, string> template) { }")]
    [InlineData("private delegate string Other(int value); [ComposeSlot] private static void Row(Item target, Other template) { }")]
    [InlineData("private class Secret { } [ComposeSlot] private static void Row(Item target, CompositionTemplate<Secret, string> template) { }")]
    [InlineData("[ComposeSlot] private static void Invoke(Item target, CompositionTemplate<int, string> template) { }")]
    [InlineData("[ComposeSlot, ComposeAction] private static void Row(Item target, CompositionTemplate<int, string> template) { }")]
    [InlineData("[ComposeSlot] private static void Row(Item target, CompositionTemplate<int, string> template) { } [ComposeSlot] private static void Row(Item target, CompositionTemplate<string, string> template) { }")]
    public void InvalidTemplateSlotsAreDiagnosed(string declaration)
    {
        GeneratorDriverRunResult result = Run(TemplateHost + declaration + " } } }", out _);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "LYC001");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "AD0001");
        Assert.Empty(result.GeneratedTrees);
    }

    /// <summary>Checks a consumer's identically named delegate cannot spoof the runtime contract.</summary>
    [Fact]
    public void LookalikeTemplateSymbolIsRejected()
    {
        string source = TemplateHost
            + "[ComposeSlot] private static void Row(Item target, CompositionTemplate<int, string> template) { } } } } namespace Lumyte.Composition { public delegate TResult CompositionTemplate<T, TResult>(T value); }";
        GeneratorDriverRunResult result = Run(source, out _);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "LYC001");
        Assert.Empty(result.GeneratedTrees);
    }

    /// <summary>Checks C# rejects mismatched values at the typed slot boundary.</summary>
    /// <param name="value">An invalid indexer expression.</param>
    [Theory]
    [InlineData("(string value) => value")]
    [InlineData("(a, b) => a.ToString()")]
    [InlineData("value => 42")]
    [InlineData("42")]
    [InlineData("new System.Func<int, string>(value => value.ToString())")]
    [InlineData("")]
    [InlineData("string.Empty, string.Empty")]
    public void TemplateSlotsRejectMismatchedInputs(string value)
    {
        string source = TemplateHost + "[ComposeSlot] private static void Row(Item target, CompositionTemplate<int, string> template) { } } } } public class Usage { public object Build() => Compose.Item()[Compose.Item.Row()[" + value + "]]; }";
        GeneratorDriverRunResult result = Run(source, out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Checks assignments remain specific to their component.</summary>
    [Fact]
    public void TemplateAssignmentRejectsAnotherTarget()
    {
        string source = TemplateHost + "[ComposeSlot] private static void Row(Item target, CompositionTemplate<int, string> template) { } } [Composable] public partial class Other { [ComposeSlot] private static void Row(Other target, CompositionTemplate<int, string> template) { } } } } public class Usage { public object Build() => Compose.Other()[Compose.Item.Row()[value => value.ToString()]]; }";
        GeneratorDriverRunResult result = Run(source, out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Checks invoking a template constructs but does not evaluate a function declaration.</summary>
    [Fact]
    public void TemplateInvocationDoesNotEvaluateComposableFunctions()
    {
        const string Source = """
            using Lumyte.Composition;
            [ComposeFunctionAdapter(typeof(Function))]
            public class Node { public string? Key { get; set; } }
            public abstract class Function : Node
            {
                protected Function() { }
                protected abstract Node Build();
                public Node Evaluate() => Build();
            }
            public static partial class Compose
            {
                public static partial class Definitions
                {
                    public static int Calls;
                    [Composable] public static Node Greeting(string label) { Calls++; return new Node { Key = label }; }
                    [Composable] public partial class Holder
                    {
                        public CompositionTemplate<string, Node>? Template { get; private set; }
                        [ComposeSlot] private static void Body(Holder target, CompositionTemplate<string, Node> template) => target.Template = template;
                    }
                }
            }
            public static class Usage
            {
                public static bool Check()
                {
                    var holder = Compose.Holder()[Compose.Holder.Body()[label => Compose.Greeting(label)]];
                    if (Compose.Definitions.Calls != 0) return false;
                    var declaration = holder.Template!("hello");
                    if (Compose.Definitions.Calls != 0 || declaration is not Function function) return false;
                    return function.Evaluate().Key == "hello" && Compose.Definitions.Calls == 1;
                }
            }
            """;
        Assert.True(ExecuteTemplateConsumer(Source));
    }

    /// <summary>Checks inherited content and Item slots retain compilable indexer metadata.</summary>
    /// <param name="baseMembers">The base component members.</param>
    /// <param name="derivedMembers">The derived component members.</param>
    /// <param name="usage">The generated-factory usage to compile.</param>
    [Theory]
    [InlineData("[ComposeContent] public int[] Children { get; set; } = [];", "[ComposeSlot] private static void Item(Derived target, CompositionTemplate<int, string> template) { }", "Compose.Derived()[1, Compose.Derived.Item()[value => value.ToString()]]")]
    [InlineData("[ComposeContent] public int[] Children { get; set; } = []; [ComposeSlot] private static void Item(Base target, CompositionTemplate<int, string> template) { }", "[ComposeSlot] private static void Header(Derived target, int[] children) { }", "Compose.Derived()[1, Compose.Derived.Header()[2]]")]
    [InlineData("[ComposeSlot] private static void Item(Base target, CompositionTemplate<int, string> template) { }", "[ComposeContent] public int[] Children { get; set; } = [];", "Compose.Derived()[1, 2]")]
    public void InheritedTemplateItemSlotsCompile(string baseMembers, string derivedMembers, string usage)
    {
        string source = "using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { [Composable] public partial class Base { "
            + baseMembers + " } [Composable] public partial class Derived : Base { " + derivedMembers
            + " } } } public static class Usage { public static object Build() => " + usage + "; }";
        GeneratorDriverRunResult result = Run(source, out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
    }

    /// <summary>Checks repeated private slots explicitly hide only their generated bridges across three levels.</summary>
    /// <param name="slotType">The existing child or typed template contract.</param>
    /// <param name="slotName">The repeated slot name.</param>
    [Theory]
    [InlineData("CompositionTemplate<T, Base<T>, Widget>", "Item")]
    [InlineData("Widget[]", "Background")]
    public void ReexportedSlotBridgesHideAccessibleSourceBridges(string slotType, string slotName)
    {
        string source = "using Lumyte.Composition; public class Widget { } public static partial class Compose { public static partial class Definitions { "
            + "[Composable] public partial class Base<T> where T : notnull { [ComposeSlot] private static void " + slotName + "(Base<T> target, " + slotType + " value) { } } "
            + "[Composable] public partial class Derived<T> : Base<T> where T : notnull { [ComposeSlot] private static void " + slotName + "(Derived<T> target, " + slotType + " value) { } } "
            + "[Composable] public partial class Leaf<T> : Derived<T> where T : notnull { [ComposeSlot] private static void " + slotName + "(Leaf<T> target, " + slotType + " value) { } } } }";
        GeneratorDriverRunResult result = Run(source, out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        Assert.Equal(2, result.GeneratedTrees.Count(tree => tree.ToString().Contains("internal new static", StringComparison.Ordinal)));
        AssertOriginalSlotBridgeWouldWarn(output);
    }

    /// <summary>Checks an inaccessible compiled internal bridge does not induce an unnecessary new modifier.</summary>
    /// <param name="friend">Whether the compiled base grants internal access to the consumer.</param>
    /// <param name="slotType">The child or typed template contract.</param>
    [Theory]
    [InlineData(false, "CompositionTemplate<int, string, object>")]
    [InlineData(true, "CompositionTemplate<int, string, object>")]
    [InlineData(false, "object[]")]
    [InlineData(true, "object[]")]
    public void ReexportedSlotBridgesRespectCompiledBaseAccessibility(bool friend, string slotType)
    {
        string baseSource = "using Lumyte.Composition; "
            + (friend ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"SlotConsumer\")] " : string.Empty)
            + "namespace Library { public static partial class Compose { public static partial class Definitions { [Composable] public partial class Base { [ComposeSlot] private static void Row(Base target, " + slotType + " value) { } } } } }";
        GeneratorDriverRunResult baseResult = Run(baseSource, out Compilation baseOutput);
        Assert.Empty(baseResult.Diagnostics);
        Assert.DoesNotContain(baseOutput.GetDiagnostics(), diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emit = baseOutput.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        string source = "using Lumyte.Composition; public static partial class Compose { public static partial class Definitions { [Composable] public partial class Derived : Library.Compose.Definitions.Base { [ComposeSlot] private static void Row(Derived target, " + slotType + " value) { } } } }";
        var options = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "SlotConsumer",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, options)],
            baseOutput.References.Append(MetadataReference.CreateFromImage(stream.ToArray())),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
            [new Generators.CompositionGenerator().AsSourceGenerator()], parseOptions: options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        Assert.Equal(friend, result.GeneratedTrees.Single().ToString().Contains("internal new static", StringComparison.Ordinal));
        if (friend)
        {
            AssertOriginalSlotBridgeWouldWarn(output);
        }
    }

    private static void AssertOriginalSlotBridgeWouldWarn(Compilation output)
    {
        // Recreate the old generator output without weakening the passing compilation.
        Compilation original = output;
        foreach (SyntaxTree tree in output.SyntaxTrees.Where(tree => tree.ToString().Contains("internal new static", StringComparison.Ordinal)))
        {
            SyntaxTree oldTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                tree.ToString().Replace("internal new static", "internal static", StringComparison.Ordinal),
                (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)tree.Options);
            original = original.ReplaceSyntaxTree(tree, oldTree);
        }

        Assert.Contains(original.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0108" && diagnostic.GetMessage().Contains("__LumyteSlot", StringComparison.Ordinal));
    }

    private static bool ExecuteTemplateConsumer(string source)
    {
        GeneratorDriverRunResult result = Run(source, out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("TemplateConsumer", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(stream);
            return (bool)assembly.GetType("Usage")!.GetMethod("Check")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
