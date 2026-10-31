using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Lumyte.Composition.Tests;

/// <summary>Checks adapter contract regressions reported during the standalone Composition review.</summary>
public sealed partial class GeneratorTests
{
    /// <summary>Checks omitted function arguments skip attachment while explicit default values attach.</summary>
    [Fact]
    public void OmittedFunctionArgumentsDoNotAttach()
    {
        string source = AdapterConsumer.Replace("ValueFunction(int value)", "ValueFunction(int value = 7)", StringComparison.Ordinal)
            .Replace("where TTarget : class { }\n    public static implicit operator ValueArgument", "where TTarget : class { if (_read is null) throw new InvalidOperationException(\"omitted\"); Counts.Attached++; }\n    public static implicit operator ValueArgument", StringComparison.Ordinal);
        const string Body = """
            var omitted = (Compose.Definitions.FunctionNode)Compose.ValueFunction();
            if (omitted.Evaluate().Value != 7 || Counts.Attached != 0) return false;
            var explicitDefault = (Compose.Definitions.FunctionNode)Compose.ValueFunction(value: 0);
            if (explicitDefault.Evaluate().Value != 0 || Counts.Attached != 1) return false;
            var explicitValue = (Compose.Definitions.FunctionNode)Compose.ValueFunction(value: 7);
            return explicitValue.Evaluate().Value == 7 && Counts.Attached == 2 && Compose.Item().Value == 5;
            """;
        VerifyAdapterConsumer(source + " public static class Counts { public static int Attached; }", Body);
    }

    /// <summary>Checks explicit interface members are called for classes and functions, preserving struct mutations.</summary>
    /// <param name="mutable">Whether resolving the argument mutates its local state.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitInterfaceAdaptersWorkWithoutLosingState(bool mutable)
    {
        string source = AdapterConsumer.Replace("public bool IsSpecified => _read is not null;", "bool IComposeArgument<T>.IsSpecified => _read is not null;", StringComparison.Ordinal)
            .Replace("public T GetInitialValue() => _read!();", "T IComposeArgument<T>.GetInitialValue() => _read!();", StringComparison.Ordinal)
            .Replace("public void AttachTo<TTarget>(TTarget target, object key, T initial, Action<TTarget, T> setter) where TTarget : class { }", "void IComposeArgument<T>.AttachTo<TTarget>(TTarget target, object key, T initial, Action<TTarget, T> setter) { if (_read is null) throw new InvalidOperationException(\"omitted\"); }", StringComparison.Ordinal);
        if (mutable)
        {
            source = source.Replace("readonly struct ValueArgument<T>", "struct ValueArgument<T>", StringComparison.Ordinal)
                .Replace("private readonly Func<T>? _read;", "private Func<T>? _read; private int _reads;", StringComparison.Ordinal)
                .Replace("T IComposeArgument<T>.GetInitialValue() => _read!();", "T IComposeArgument<T>.GetInitialValue() { _reads++; return _read!(); }", StringComparison.Ordinal)
                .Replace("if (_read is null) throw new InvalidOperationException(\"omitted\");", "if (_read is null || _reads != 1) throw new InvalidOperationException(\"adapter state was copied\");", StringComparison.Ordinal);
        }

        const string Body = """
            var item = Compose.Item(value: 12);
            var function = (Compose.Definitions.FunctionNode)Compose.ValueFunction(value: 13);
            return item.Value == 12 && function.Evaluate().Value == 13 && Compose.Item().Value == 5;
            """;
        VerifyAdapterConsumer(source, Body);
    }

    /// <summary>Checks exact array mappings precede the fallback for classes and functions.</summary>
    /// <param name="fallback">Whether a generic fallback also exists.</param>
    /// <param name="rectangular">Whether the declared value is a rectangular array.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExactArrayMappingsAreSelected(bool fallback, bool rectangular)
    {
        string array = rectangular ? "int[,]" : "int[]";
        string empty = rectangular ? "new int[0, 0]" : "Array.Empty<int>()";
        string allocation = rectangular ? "new int[count, 1]" : "new int[count]";
        string source = AdapterConsumer.Replace("public class Settings", "[ComposeArgumentAdapter(typeof(" + array + "), typeof(ArrayArgument))] public class Settings", StringComparison.Ordinal)
            .Replace("public Func<object> Factory", "[ComposeParameter] public " + array + " Items { get; set; } = " + empty + "; public Func<object> Factory", StringComparison.Ordinal)
            .Replace("[Composable] public static Node ValueFunction", "[Composable] public static Node ArrayFunction(" + array + " items) => new() { Value = items.Length }; [Composable] public static Node ValueFunction", StringComparison.Ordinal);
        if (!fallback)
        {
            source = source.Replace("[ComposeArgumentAdapter(typeof(ValueArgument<>))]", string.Empty, StringComparison.Ordinal);
        }

        source += " public readonly struct ArrayArgument : IComposeArgument<" + array + "> { private readonly " + array + "? _items; private ArrayArgument(" + array + " items) => _items = items; public bool IsSpecified => _items is not null; public " + array + " GetInitialValue() => _items!; public void AttachTo<TTarget>(TTarget target, object key, " + array + " initial, Action<TTarget, " + array + "> setter) where TTarget : class { } public static implicit operator ArrayArgument(int count) => new(" + allocation + "); }";
        const string Body = """
            var item = Compose.Item(items: 3);
            var function = (Compose.Definitions.FunctionNode)Compose.ArrayFunction(items: 4);
            return item.Items.Length == 3 && function.Evaluate().Value == 4;
            """;
        VerifyAdapterConsumer(source, Body);
    }

    /// <summary>Checks constrained dispatch honors adapter nullability when a nullable target accepts non-null results.</summary>
    [Fact]
    public void NullableTargetsAcceptStrongerAdapterContracts()
    {
        string source = AdapterConsumer.Replace("public string Text { get; set; } = \"default\";", "public string? Text { get; set; } = \"default\";", StringComparison.Ordinal)
            .Replace("TextFunction(string text) => new() { Text = text };", "TextFunction(string? text = null) => new() { Text = text ?? \"fallback\" };", StringComparison.Ordinal);
        const string Body = """
            var omitted = (Compose.Definitions.FunctionNode)Compose.TextFunction();
            var specified = (Compose.Definitions.FunctionNode)Compose.TextFunction(text: "hello");
            return omitted.Evaluate().Text == "fallback" && specified.Evaluate().Text == "hello" && Compose.Item(text: "hello").Text == "hello";
            """;
        VerifyAdapterConsumer(source, Body);
    }

    private static void VerifyAdapterConsumer(string source, string body)
    {
        GeneratorDriverRunResult result = Run(source + " public static class Usage { public static bool Verify() { " + body + " } }", out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
        using var stream = new MemoryStream();
        Assert.True(output.Emit(stream).Success);
        var assembly = Assembly.Load(stream.ToArray());
        Assert.Equal(true, assembly.GetType("Usage")!.GetMethod("Verify")!.Invoke(null, null));
    }
}
