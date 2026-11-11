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

    /// <summary>Real C# clients compile and execute temporary nested setters and every supported property kind.</summary>
    /// <param name="collidingMember">Whether a logical member uses the preferred root view type name.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedPropertyClientCompilesAndExecutesWithoutLogicalIndexerCopies(bool collidingMember)
    {
        const string Source = """
            using System;
            using System.Numerics;
            using System.Collections.Generic;
            using Lumyte.Graphics.Abstractions;
            public readonly record struct Leaf(float Value);
            public readonly record struct Branch(Leaf Child);
            public readonly partial record struct Data(int Signed, uint Unsigned, float Scalar,
                Vector2 Pair, Vector3 Triple, Vector4 Color, Matrix4x4 Transform,
                Branch Nested, IGpuRef<float> Buffer) : IShaderData;
            public sealed class Reference : IGpuRef<float>
            {
                public ulong Count => 1;
                public IGpuRef<float> GetElement(ulong index) => this;
            }
            public sealed class Memory : ShaderDataStorage<Data>
            {
                private readonly Dictionary<string, object>[] values;
                public int Captures;
                public int NumericSets;
                public int ReferenceSets;
                public override int Count { get; }
                public Memory(int count)
                {
                    Count = count;
                    values = new Dictionary<string, object>[Math.Max(0, count)];
                    for (int index = 0; index < values.Length; index++) values[index] = new();
                }
                public override V GetValue<V>(int index, ShaderDataValueMember<Data, V> member)
                    => (V)values[index][member.Path];
                public override IGpuRef<R> GetReference<R>(int index, ShaderDataReferenceMember<Data, R> member)
                    => (IGpuRef<R>)values[index][member.Path];
                public override void SetValue<V>(int index, ShaderDataValueMember<Data, V> member, in V value)
                {
                    NumericSets++;
                    values[index][member.Path] = value;
                }
                public override void SetReference<R>(int index, ShaderDataReferenceMember<Data, R> member, IGpuRef<R> value)
                {
                    ReferenceSets++;
                    values[index][member.Path] = value;
                }
                public override void Set(int index, in Data value)
                {
                    Captures++;
                    _ = IShaderArguments.Capture(in value);
                    values[index]["Signed"] = value.Signed;
                    values[index]["Unsigned"] = value.Unsigned;
                    values[index]["Scalar"] = value.Scalar;
                    values[index]["Pair"] = value.Pair;
                    values[index]["Triple"] = value.Triple;
                    values[index]["Color"] = value.Color;
                    values[index]["Transform"] = value.Transform;
                    values[index]["Nested.Child.Value"] = value.Nested.Child.Value;
                    values[index]["Buffer"] = value.Buffer;
                }
            }
            public sealed class Buffer : IGraphicsShaderDataBuffer<Data>
            {
                public Memory? Created;
                public ulong Count => 2;
                public ulong SizeInBytes => 512;
                public ulong ShaderElementStrideInBytes => 256;
                public MemoryPreference Memory => MemoryPreference.Upload;
                public bool IsMapped => true;
                public ShaderDataStorage<Data> CreateStorage(ReadOnlySpan<Data> initial)
                {
                    Created = new Memory(initial.Length);
                    for (int index = 0; index < initial.Length; index++) Created.Set(index, initial[index]);
                    return Created;
                }
                public void CopyFrom(ReadOnlySpan<Data> source, ulong elementOffset = 0) => throw new NotSupportedException();
                public System.Threading.Tasks.ValueTask MapAsync(System.Threading.CancellationToken cancellationToken = default)
                    => System.Threading.Tasks.ValueTask.CompletedTask;
                public void Unmap() { }
                public ShaderDataSlice<Data> SliceElements(ulong offset, ulong count) => new(this, offset, count);
                public void Dispose() { }
            }
            public static class Probe
            {
                private static T Default<T>(T value) => default!;
                private static void Check(bool condition)
                {
                    if (!condition) throw new InvalidOperationException("Generated property client assertion failed.");
                }
                private static void Throws<E>(Action action) where E : Exception
                {
                    try { action(); }
                    catch (E) { return; }
                    throw new InvalidOperationException("Expected " + typeof(E).Name);
                }
                public static object Run()
                {
                    var reference = new Reference();
                    var initial = new Data(1, 2, 3, Vector2.One, Vector3.One, Vector4.One,
                        Matrix4x4.Identity, new Branch(new Leaf(4)), reference);
                    var buffer = new Buffer();
                    Data.Storage storage = Data.CreateStorage(buffer, new[] { initial, initial });
                    Memory memory = buffer.Created!;
                    Check(storage.Count == 2 && ReferenceEquals(memory, storage.Data));
                    ShaderDataStorage<Data> unwrapped = storage;
                    Check(ReferenceEquals(memory, unwrapped));
                    var root = storage[0];
                    var branch = root.Nested;
                    var child = branch.Child;
                    ref readonly var cached = ref storage[0];
                    ref readonly var cachedChild = ref cached.Nested.Child;
                    storage[0].Nested.Child.Value = 9;
                    Check(child.Value == 9 && cachedChild.Value == 9);
                    child.Value = 10;
                    cachedChild.Value = 11;
                    Check(storage[0].Nested.Child.Value == 11 && storage[1].Nested.Child.Value == 4);
                    storage[0].Signed = -7;
                    storage[0].Unsigned = 8;
                    storage[0].Scalar = 9;
                    storage[0].Pair = new Vector2(10);
                    storage[0].Triple = new Vector3(11);
                    storage[0].Color = new Vector4(12);
                    Matrix4x4 matrix = Matrix4x4.CreateTranslation(1, 2, 3);
                    cached.Transform = matrix;
                    var replacement = new Reference();
                    root.Buffer = replacement;
                    Check(root.Signed == -7 && root.Unsigned == 8 && root.Scalar == 9);
                    Check(root.Pair == new Vector2(10) && root.Triple == new Vector3(11));
                    Check(root.Color == new Vector4(12) && root.Transform == matrix);
                    Check(ReferenceEquals(cached.Buffer, replacement));
                    Check(memory.Captures == 2 && memory.NumericSets == 10 && memory.ReferenceSets == 1);
                    storage.Set(0, initial);
                    Check(memory.Captures == 3 && child.Value == 4 && root.Signed == 1);
                    Check(ReferenceEquals(root.Buffer, reference));
                    var invalid = Default(storage[0]);
                    var invalidChild = Default(child);
                    Throws<InvalidOperationException>(() => { _ = invalid.Nested; });
                    Throws<InvalidOperationException>(() => { _ = invalid.Signed; });
                    Throws<InvalidOperationException>(() => invalid.Signed = 1);
                    Throws<InvalidOperationException>(() => { _ = invalid.Buffer; });
                    Throws<InvalidOperationException>(() => invalid.Buffer = reference);
                    Throws<InvalidOperationException>(() => { _ = invalidChild.Value; });
                    Throws<InvalidOperationException>(() => invalidChild.Value = 1);
                    Throws<ArgumentOutOfRangeException>(() => { _ = storage[-1]; });
                    Throws<ArgumentOutOfRangeException>(() => { _ = storage[storage.Count]; });
                    Throws<ArgumentOutOfRangeException>(() => new Data.Storage(new Memory(-1)));
                    Throws<ArgumentNullException>(() => new Data.Storage(null!));
                    Throws<ArgumentNullException>(() => Data.CreateStorage(null!, ReadOnlySpan<Data>.Empty));
                    Throws<ArgumentNullException>(() => { ShaderDataStorage<Data> ignored = (Data.Storage)null!; });
                    var temporaryBuffer = new Buffer();
                    Data.CreateStorage(temporaryBuffer, new[] { initial })[0].Nested.Child.Value = 19;
                    Check(temporaryBuffer.Created!.GetValue(0, Data.StorageMembers.Nested.Child.Value) == 19);
                    new Data.Storage(memory)[1].Color = new Vector4(23);
                    Check(storage[1].Color == new Vector4(23));
                    return true;
                }
            }
            """;
        string source = collidingMember ? Source.Replace("Signed", "Element", StringComparison.Ordinal) : Source;
        (GeneratorDriverRunResult Run, Compilation Output) result = Run(source);
        AssertCompiles(result);
        Assert.True(Execute<bool>(result.Output));
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
