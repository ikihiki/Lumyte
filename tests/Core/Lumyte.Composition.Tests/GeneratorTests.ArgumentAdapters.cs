using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Lumyte.Composition.Tests;

/// <summary>Tests independently selected declaration and construction argument types.</summary>
public sealed partial class GeneratorTests
{
    private const string AdapterConsumer = """
        using System;
        using System.Collections.Generic;
        using System.Globalization;
        using System.Runtime.CompilerServices;
        using Lumyte.Composition;

        public sealed class Source<T>(Func<T> read)
        {
            public T Value => read();
            public static Source<T> Constant(T value) => new(() => value);
        }

        public readonly struct ValueArgument<T> : IComposeArgument<T>
        {
            private readonly Func<T>? _read;
            private ValueArgument(Func<T> read) => _read = read;
            public bool IsSpecified => _read is not null;
            public T GetInitialValue() => _read!();
            public void AttachTo<TTarget>(TTarget target, object key, T initial, Action<TTarget, T> setter) where TTarget : class { }
            public static implicit operator ValueArgument<T>(T value) => new(() => value);
            public static implicit operator ValueArgument<T>(Source<T> source) => new(() => source.Value);
        }

        public readonly struct SourceArgument<T> : IComposeArgument<Source<T>>
        {
            private readonly Source<T>? _source;
            private SourceArgument(Source<T> source) => _source = source;
            public bool IsSpecified => _source is not null;
            public Source<T> GetInitialValue() => _source!;
            public void AttachTo<TTarget>(TTarget target, object key, Source<T> initial, Action<TTarget, Source<T>> setter) where TTarget : class { }
            public static implicit operator SourceArgument<T>(T value) => new(Source<T>.Constant(value));
            public static implicit operator SourceArgument<T>(Source<T> source) => new(source);
        }

        [InterpolatedStringHandler]
        public struct TextArgument : IComposeArgument<string>
        {
            private readonly List<Func<string>> _parts;
            public TextArgument(int literalLength, int formattedCount) => _parts = new();
            public bool IsSpecified => _parts is not null;
            public string GetInitialValue() => string.Concat(_parts.ConvertAll(read => read()));
            public void AttachTo<TTarget>(TTarget target, object key, string initial, Action<TTarget, string> setter) where TTarget : class { }
            public void AppendLiteral(string text) => _parts.Add(() => text);
            public void AppendFormatted<T>(T value, int alignment = 0, string? format = null)
            {
                string part = Format(value, alignment, format);
                _parts.Add(() => part);
            }
            public void AppendFormatted<T>(T value, string? format) => AppendFormatted(value, 0, format);
            public void AppendFormatted<T>(Source<T> source, int alignment = 0, string? format = null)
                => _parts.Add(() => Format(source.Value, alignment, format));
            public void AppendFormatted<T>(Source<T> source, string? format) => AppendFormatted(source, 0, format);
            public static implicit operator TextArgument(string value)
            {
                var text = new TextArgument(0, 0);
                text.AppendLiteral(value);
                return text;
            }
            public static implicit operator TextArgument(Source<string> source)
            {
                var text = new TextArgument(0, 1);
                text.AppendFormatted(source);
                return text;
            }
            private static string Format<T>(T value, int alignment, string? format)
                => string.Format(CultureInfo.InvariantCulture, "{0," + alignment + ":" + format + "}", value);
        }

        [InterpolatedStringHandler]
        public struct SourceTextArgument : IComposeArgument<Source<string>>
        {
            private TextArgument _text;
            private Source<string>? _source;
            public SourceTextArgument(int literalLength, int formattedCount)
            {
                _text = new(literalLength, formattedCount);
                _source = null;
            }
            public bool IsSpecified => _source is not null || _text.IsSpecified;
            public Source<string> GetInitialValue()
            {
                TextArgument text = _text;
                return _source ?? new Source<string>(() => text.GetInitialValue());
            }
            public void AttachTo<TTarget>(TTarget target, object key, Source<string> initial, Action<TTarget, Source<string>> setter) where TTarget : class { }
            public void AppendLiteral(string text) => _text.AppendLiteral(text);
            public void AppendFormatted<T>(T value, int alignment = 0, string? format = null) => _text.AppendFormatted(value, alignment, format);
            public void AppendFormatted<T>(T value, string? format) => _text.AppendFormatted(value, format);
            public void AppendFormatted<T>(Source<T> source, int alignment = 0, string? format = null) => _text.AppendFormatted(source, alignment, format);
            public void AppendFormatted<T>(Source<T> source, string? format) => _text.AppendFormatted(source, format);
            public static implicit operator SourceTextArgument(string value) => new() { _text = value };
            public static implicit operator SourceTextArgument(Source<string> source) => new() { _source = source };
        }

        [ComposeArgumentAdapter(typeof(ValueArgument<>))]
        [ComposeArgumentAdapter(typeof(Source<>), typeof(SourceArgument<>))]
        [ComposeArgumentAdapter(typeof(string), typeof(TextArgument))]
        [ComposeArgumentAdapter(typeof(Source<string>), typeof(SourceTextArgument))]
        public class Settings { }

        public static partial class Compose
        {
            public static partial class Definitions
            {
                [Composable]
                public partial class Item : Settings, ICompositionRebuildable
                {
                    [ComposeParameter] public int Value { get; set; } = 5;
                    [ComposeParameter] public Source<int> Source { get; set; } = Source<int>.Constant(5);
                    [ComposeParameter] public string Text { get; set; } = "default";
                    [ComposeParameter] public Source<string> SourceText { get; set; } = Source<string>.Constant("default");
                    public Func<object> Factory { get; private set; } = null!;
                    void ICompositionRebuildable.SetCompositionFactory(Func<object> factory) => Factory = factory;
                }

                [ComposeFunctionAdapter(typeof(FunctionNode))]
                public class Node : Settings
                {
                    public string? Key { get; set; }
                    public int Value { get; set; }
                    public string Text { get; set; } = "";
                }
                public abstract class FunctionNode : Node
                {
                    protected FunctionNode() { }
                    protected abstract Node Build();
                    public Node Evaluate() => Build();
                }
                [Composable] public static Node ValueFunction(int value) => new() { Value = value };
                [Composable] public static Node SourceFunction(Source<int> value) => new() { Value = value.Value };
                [Composable] public static Node TextFunction(string text) => new() { Text = text };
                [Composable] public static Node SourceTextFunction(Source<string> text) => new() { Text = text.Value };
            }
        }
        """;

    /// <summary>Executes generated factories accepting constants, sources, and direct interpolation.</summary>
    /// <param name="body">A consumer body returning whether its observations match.</param>
    [Theory]
    [InlineData("return Compose.Item(value: 12, source: 12).Value == 12 && Compose.Item(source: 12).Source.Value == 12;")]
    [InlineData("int value = 12; var source = new Source<int>(() => value); var item = Compose.Item(value: source, source: source); value = 20; return item.Value == 12 && ReferenceEquals(item.Source, source) && item.Source.Value == 20 && ((Compose.Definitions.Item)item.Factory()).Value == 20;")]
    [InlineData("return Compose.Item(text: \"literal\", sourceText: \"literal\").Text == \"literal\" && Compose.Item(sourceText: \"literal\").SourceText.Value == \"literal\";")]
    [InlineData("string value = \"before\"; var source = new Source<string>(() => value); var item = Compose.Item(text: source, sourceText: source); value = \"after\"; return item.Text == \"before\" && ReferenceEquals(item.SourceText, source) && ((Compose.Definitions.Item)item.Factory()).Text == \"after\";")]
    [InlineData("int value = 12; var source = new Source<int>(() => value); var item = Compose.Item(text: $\"count: {source,6:N0}\", sourceText: $\"count: {source,6:N0}\"); value = 20; return item.Text == \"count:     12\" && item.SourceText.Value == \"count:     20\" && ((Compose.Definitions.Item)item.Factory()).Text == \"count:     20\";")]
    [InlineData("var a = new Source<int>(() => 12); var b = new Source<string>(() => \"ok\"); return Compose.Item(text: $\"{{{a:N0}}} {b} {3} {(string?)null}\").Text == \"{12} ok 3 \";")]
    [InlineData("return ((Compose.Definitions.FunctionNode)Compose.ValueFunction(value: 12)).Evaluate().Value == 12 && ((Compose.Definitions.FunctionNode)Compose.SourceFunction(value: 12)).Evaluate().Value == 12;")]
    [InlineData("var source = new Source<int>(() => 12); return ((Compose.Definitions.FunctionNode)Compose.ValueFunction(value: source)).Evaluate().Value == 12 && ((Compose.Definitions.FunctionNode)Compose.SourceFunction(value: source)).Evaluate().Value == 12;")]
    [InlineData("var source = new Source<int>(() => 12); return ((Compose.Definitions.FunctionNode)Compose.TextFunction(text: $\"count: {source}\")).Evaluate().Text == \"count: 12\" && ((Compose.Definitions.FunctionNode)Compose.SourceTextFunction(text: $\"count: {source}\")).Evaluate().Text == \"count: 12\";")]
    [InlineData("return Compose.Item().Value == 5 && Compose.Item().Text == \"default\";")]
    public void TypeMappingsAcceptInputsIndependentlyOfDeclaredTypes(string body)
    {
        GeneratorDriverRunResult result = Run(AdapterConsumer + " public static class Usage { public static bool Verify() { " + body + " } }", out Compilation output);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        using var stream = new MemoryStream();
        Assert.True(output.Emit(stream).Success);
        var assembly = Assembly.Load(stream.ToArray());
        Assert.Equal(true, assembly.GetType("Usage")!.GetMethod("Verify")!.Invoke(null, null));
    }

    /// <summary>Reports unsupported mappings as diagnostics instead of generator failures.</summary>
    /// <param name="mapping">An additional malformed or conflicting mapping.</param>
    [Theory]
    [InlineData("[ComposeArgumentAdapter(typeof(int), typeof(TextArgument))]")]
    [InlineData("[ComposeArgumentAdapter(typeof(int), typeof(ValueArgument<>))]")]
    [InlineData("[ComposeArgumentAdapter(typeof(Source<>), typeof(TextArgument))]")]
    [InlineData("[ComposeArgumentAdapter(typeof(Source<>), typeof(ValueArgument<>))]")]
    [InlineData("[ComposeArgumentAdapter(typeof(SourceArgument<>))]")]
    [InlineData("[ComposeArgumentAdapter(typeof(string), typeof(int))]")]
    [InlineData("[ComposeArgumentAdapter(typeof(string), typeof(string))]")]
    [InlineData("[ComposeArgumentAdapter(null, typeof(TextArgument))]")]
    public void InvalidTypeMappingsProduceDiagnostics(string mapping)
    {
        string source = AdapterConsumer.Replace("public class Settings", mapping + " public class Settings", StringComparison.Ordinal);
        GeneratorDriverRunResult result = Run(source, out _);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "LYC001");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "AD0001");
    }

    /// <summary>Validates constraints on the selected generic argument instead of the wrapper type.</summary>
    [Fact]
    public void GenericMappingConstraintsAreValidatedForFunctions()
    {
        string source = AdapterConsumer.Replace("struct SourceArgument<T> : IComposeArgument<Source<T>>", "struct SourceArgument<T> : IComposeArgument<Source<T>> where T : class", StringComparison.Ordinal);
        GeneratorDriverRunResult result = Run(source, out _);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "LYC001" && diagnostic.GetMessage().Contains("constraints", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "AD0001");
    }
}
