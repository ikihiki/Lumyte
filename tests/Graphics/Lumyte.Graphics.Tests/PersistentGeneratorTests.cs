using System.Collections;
using System.Numerics;
using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Compiles the real shader generator output and exercises persistent metadata without a GPU.</summary>
public sealed class PersistentGeneratorTests
{
    /// <summary>Numeric leaves and nested tokens retain their exact paths and CLR types.</summary>
    [Fact]
    public void NumericAndNestedMembersCompileWithTypedTokensAndMatchingCapture()
    {
        const string Source = """
            using System.Numerics;
            using Lumyte.Graphics.Abstractions;
            public readonly record struct ChildValue(float Value);
            public readonly partial record struct Data(
                int Signed, uint Unsigned, float Scalar, Vector2 Pair, Vector3 Triple,
                Vector4 Color, Matrix4x4 Transform, ChildValue Child) : IShaderData;
            public static class Probe
            {
                private static System.Collections.Generic.IReadOnlyList<ShaderDataMember> Schema<T>()
                    where T : struct, IShaderData => T.StorageSchema;
                public static object Run()
                {
                    ShaderDataValueMember<Data, int> signed = Data.StorageMembers.Signed;
                    ShaderDataValueMember<Data, uint> unsigned = Data.StorageMembers.Unsigned;
                    ShaderDataValueMember<Data, float> scalar = Data.StorageMembers.Scalar;
                    ShaderDataValueMember<Data, Vector2> pair = Data.StorageMembers.Pair;
                    ShaderDataValueMember<Data, Vector3> triple = Data.StorageMembers.Triple;
                    ShaderDataValueMember<Data, Vector4> color = Data.StorageMembers.Color;
                    ShaderDataValueMember<Data, Matrix4x4> transform = Data.StorageMembers.Transform;
                    ShaderDataValueMember<Data, float> child = Data.StorageMembers.Child.Value;
                    var value = new Data(-1, 2u, 3f, Vector2.One, Vector3.One,
                        Vector4.One, Matrix4x4.Identity, new ChildValue(9f));
                    return new object[] { Schema<Data>(), Schema<Data>(), IShaderArguments.Capture(in value),
                        new string[] { signed.Path, unsigned.Path, scalar.Path, pair.Path,
                            triple.Path, color.Path, transform.Path, child.Path } };
                }
            }
            """;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(Source);
        AssertCompiles(result);
        object[] values = Execute<object[]>(result.Output);
        IReadOnlyList<ShaderDataMember> schema = Assert.IsAssignableFrom<IReadOnlyList<ShaderDataMember>>(values[0]);
        Assert.Same(schema, values[1]);
        string[] paths = ["Signed", "Unsigned", "Scalar", "Pair", "Triple", "Color", "Transform", "Child.Value"];
        Type[] types = [typeof(int), typeof(uint), typeof(float), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Matrix4x4), typeof(float)];
        Assert.Equal(paths, schema.Select(member => member.Path));
        Assert.Equal(types, schema.Select(member => member.ValueType));
        Assert.All(schema, member =>
        {
            Assert.False(member.IsReference);
            Assert.Null(member.ReferenceTarget);
        });
        Assert.Equal(paths, Assert.IsType<string[]>(values[3]));
        ShaderValueSnapshot capture = Assert.IsType<ShaderValueSnapshot>(values[2]);
        Assert.Equal("Data", capture.ShaderTypeName);
        Assert.Equal(paths, capture.Values.Select(value => value.Path));
        Assert.Equal(types, capture.Values.Select(value => value.ValueType));
        Assert.All(capture.Values, value => Assert.False(value.IsReference));

        // Returning a cached IReadOnlyList is insufficient if it still exposes a writable array.
        Assert.False(schema is ShaderDataMember[]);
        IList<ShaderDataMember> list = Assert.IsAssignableFrom<IList<ShaderDataMember>>(schema);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = new("Corrupted", typeof(int), false));
        IList untyped = Assert.IsAssignableFrom<IList>(schema);
        Assert.Throws<NotSupportedException>(() => untyped[0] = new ShaderDataMember("Corrupted", typeof(int), false));
        Assert.Equal("Signed", schema[0].Path);
    }

    /// <summary>A recursive buffer is a reference leaf, not an inline recursion cycle.</summary>
    [Fact]
    public void RecursiveBufferReferenceCompilesAndStopsAtReferenceLeaf()
    {
        const string Source = """
            using Lumyte.Graphics.Abstractions;
            [ShaderTypeName("ShaderNode")]
            public readonly partial record struct Node(float Value, IGpuRef<Node> Next) : IShaderData;
            public sealed class Reference : IGpuRef<Node>
            {
                public ulong Count => 1;
                public IGpuRef<Node> GetElement(ulong index) => this;
            }
            public static class Probe
            {
                private static System.Collections.Generic.IReadOnlyList<ShaderDataMember> Schema<T>()
                    where T : struct, IShaderData => T.StorageSchema;
                public static object Run()
                {
                    ShaderDataReferenceMember<Node, Node> token = Node.StorageMembers.Next;
                    var reference = new Reference();
                    var value = new Node(5f, reference);
                    return new object[] { Schema<Node>(), IShaderArguments.Capture(in value), reference, token.Path };
                }
            }
            """;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(Source);
        AssertCompiles(result);
        object[] values = Execute<object[]>(result.Output);
        IReadOnlyList<ShaderDataMember> schema = Assert.IsAssignableFrom<IReadOnlyList<ShaderDataMember>>(values[0]);
        Assert.Equal(["Value", "Next"], schema.Select(member => member.Path));
        ShaderDataMember next = schema[1];
        Assert.True(next.IsReference);
        Assert.Equal("ShaderNode", next.ReferenceTarget);
        Assert.Equal("Node", next.ValueType.Name);
        Assert.Equal("Next", values[3]);
        ShaderValueSnapshot capture = Assert.IsType<ShaderValueSnapshot>(values[1]);
        ShaderValue leaf = Assert.Single(capture.Values, value => value.IsReference);
        Assert.Equal("Next", leaf.Path);
        Assert.Same(values[2], leaf.Reference);
        Assert.Equal(next.ValueType, leaf.ValueType);
    }

    /// <summary>Generated reference metadata uses static interface dispatch, not reflection or a CLR type name.</summary>
    [Fact]
    public void AnnotatedReferenceTargetUsesAotStaticTypeNameDispatch()
    {
        const string Source = """
            using Lumyte.Graphics.Abstractions;
            [ShaderTypeName("AnnotatedTarget")]
            public readonly partial record struct Target(float Value) : IShaderData;
            [ShaderTypeName("IgnoredBecauseExplicit")]
            public readonly partial record struct ExplicitTarget(float Value) : IShaderData
            {
                static string IShaderArguments.ShaderTypeName => "ActualStaticTarget";
            }
            public readonly partial record struct Data(IGpuRef<Target> Buffer, IGpuRef<ExplicitTarget> Other) : IShaderData;
            public static class Probe
            {
                private static System.Collections.Generic.IReadOnlyList<ShaderDataMember> Schema<T>()
                    where T : struct, IShaderData => T.StorageSchema;
                public static object Run() => Schema<Data>();
            }
            """;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(Source);
        AssertCompiles(result);
        string generated = string.Join(Environment.NewLine, result.Run.GeneratedTrees.Select(tree => tree.ToString()));
        Assert.Contains("IShaderArguments.TypeName<global::Target>()", generated, StringComparison.Ordinal);
        Assert.Contains("IShaderArguments.TypeName<global::ExplicitTarget>()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Reflection", generated, StringComparison.Ordinal);
        IReadOnlyList<ShaderDataMember> schema = Execute<IReadOnlyList<ShaderDataMember>>(result.Output);
        Assert.Equal(["AnnotatedTarget", "ActualStaticTarget"], schema.Select(member => member.ReferenceTarget));
        Assert.All(schema, member => Assert.True(member.IsReference));
    }

    /// <summary>Logical reference targets are not inferred from the raw uint backing words.</summary>
    /// <param name="elementType">The logical buffer element CLR type.</param>
    /// <param name="shaderName">The required logical shader target name.</param>
    [Theory]
    [InlineData("float", "float")]
    [InlineData("int", "int")]
    [InlineData("uint", "uint")]
    [InlineData("System.Numerics.Vector2", "float2")]
    [InlineData("System.Numerics.Vector3", "float3")]
    [InlineData("System.Numerics.Vector4", "float4")]
    [InlineData("System.Numerics.Matrix4x4", "float4x4")]
    public void RawWordBackedReferencesKeepTheirLogicalElementTarget(string elementType, string shaderName)
    {
        string source = $$"""
            using Lumyte.Graphics.Abstractions;
            public readonly partial record struct Data(IGpuRef<{{elementType}}> Buffer) : IShaderData;
            public sealed class RawWords : IGpuRef<{{elementType}}>
            {
                public uint[] Words { get; } = new uint[16];
                public ulong Count => 1;
                public IGpuRef<{{elementType}}> GetElement(ulong index) => this;
            }
            public static class Probe
            {
                private static System.Collections.Generic.IReadOnlyList<ShaderDataMember> Schema<T>()
                    where T : struct, IShaderData => T.StorageSchema;
                public static object Run()
                {
                    ShaderDataReferenceMember<Data, {{elementType}}> token = Data.StorageMembers.Buffer;
                    var reference = new RawWords();
                    var value = new Data(reference);
                    return new object[] { Schema<Data>(), IShaderArguments.Capture(in value), reference, token.Path };
                }
            }
            """;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(source);
        AssertCompiles(result);
        object[] values = Execute<object[]>(result.Output);
        ShaderDataMember member = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ShaderDataMember>>(values[0]));
        Assert.True(member.IsReference);
        Assert.Equal(shaderName, member.ReferenceTarget);
        Assert.Equal("Buffer", member.Path);
        ShaderValueSnapshot capture = Assert.IsType<ShaderValueSnapshot>(values[1]);
        ShaderValue leaf = Assert.Single(capture.Values);
        Assert.True(leaf.IsReference);
        Assert.Equal(member.ValueType, leaf.ValueType);
        Assert.Same(values[2], leaf.Reference);
        Assert.Equal("Buffer", values[3]);
    }

    /// <summary>Unsupported declarations fail with a useful generator error and no partial output.</summary>
    /// <param name="declaration">The unsupported input declaration.</param>
    /// <param name="reason">The required diagnostic explanation.</param>
    [Theory]
    [InlineData("public readonly partial record struct Data : IShaderData { public float Value => 1f; }", "custom getters")]
    [InlineData("public readonly partial record struct Data : IShaderData { public float Value { get { return 1f; } } }", "custom getters")]
    [InlineData("public readonly partial record struct Data(string Value) : IShaderData;", "unsupported member type string")]
    [InlineData("public readonly partial record struct Data(float[] Value) : IShaderData;", "unsupported member type float[]")]
    [InlineData("public readonly record struct Data(float Value) : IShaderData;", "must be partial")]
    [InlineData("public class Outer { public readonly partial record struct Data(float Value) : IShaderData; }", "nested")]
    [InlineData("public readonly partial record struct Data<T>(float Value) : IShaderData;", "generic")]
    [InlineData("public readonly partial record struct Data(float Value) : IShaderData { public static class StorageMembers { } }", "StorageMembers")]
    [InlineData("public readonly record struct ChildValue(float Child); public readonly partial record struct Data(ChildValue Child) : IShaderData;", "Child.Child")]
    public void UnsupportedPersistentDeclarationsReportClearDiagnostic(string declaration, string reason)
    {
        (GeneratorDriverRunResult Run, Compilation Output) result = Run("using Lumyte.Graphics.Abstractions; " + declaration);
        Diagnostic diagnostic = Assert.Single(result.Run.Diagnostics);
        Assert.Equal("LUMG001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(reason, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Empty(result.Run.GeneratedTrees);
    }

    /// <summary>A handwritten codec must not silently acquire a generated persistent schema.</summary>
    /// <param name="explicitName">Whether the handwritten codec also supplies its own shader name.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandwrittenWriteRemainsWithoutGeneratedStorageSchema(bool explicitName)
    {
        string source = $$"""
            using Lumyte.Graphics.Abstractions;
            public readonly partial record struct Data(float Value) : IShaderData
            {
                {{(explicitName ? "public static string ShaderTypeName => \"Data\";" : string.Empty)}}
                public void Write<TWriter>(ref TWriter writer) where TWriter : IShaderValueWriter
                    => writer.WriteValue("Value", Value);
            }
            public static class Probe
            {
                private static System.Collections.Generic.IReadOnlyList<ShaderDataMember> Schema<T>()
                    where T : struct, IShaderData => T.StorageSchema;
                public static object Run()
                {
                    var value = new Data(7f);
                    var capture = IShaderArguments.Capture(in value);
                    bool rejected = false;
                    try { _ = Schema<Data>(); }
                    catch (System.NotSupportedException) { rejected = true; }
                    return new object[] { capture, rejected };
                }
            }
            """;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(source);
        AssertCompiles(result);
        string generated = string.Join(Environment.NewLine, result.Run.GeneratedTrees.Select(tree => tree.ToString()));
        Assert.DoesNotContain("StorageSchema", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageMembers", generated, StringComparison.Ordinal);
        object[] values = Execute<object[]>(result.Output);
        Assert.True(Assert.IsType<bool>(values[1]));
        ShaderValueSnapshot capture = Assert.IsType<ShaderValueSnapshot>(values[0]);
        Assert.Equal("Data", capture.ShaderTypeName);
        Assert.Equal("Value", Assert.Single(capture.Values).Path);
    }

    private static void AssertCompiles((GeneratorDriverRunResult Run, Compilation Output) result)
    {
        Assert.Empty(result.Run.Diagnostics);
        Diagnostic[] errors = result.Output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
    }

    // Reflection belongs only to this compilation harness; generated product code uses static dispatch.
    private static T Execute<T>(Compilation compilation)
    {
        using var stream = new MemoryStream();
        EmitResult emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        var assembly = Assembly.Load(stream.ToArray());
        MethodInfo method = assembly.GetType("Probe", throwOnError: true)!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<T>(method.Invoke(null, null));
    }

    private static (GeneratorDriverRunResult Run, Compilation Output) Run(string source)
    {
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = paths.Append(typeof(IShaderData).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path));
        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "PersistentGeneratorProbe_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, parse, "/persistent.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ShaderArgumentsGenerator().AsSourceGenerator()], parseOptions: parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (driver.GetRunResult(), output);
    }
}
