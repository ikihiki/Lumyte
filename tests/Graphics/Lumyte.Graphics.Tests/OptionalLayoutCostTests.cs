using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Guards the schema-free path independently of Debug or Release compilation.</summary>
public sealed class OptionalLayoutCostTests
{
    /// <summary>Capturing, copying, and packing never invoke a handwritten diagnostic getter.</summary>
    [Fact]
    public void CaptureCopyAndPackLeaveDiagnosticsDeferred()
    {
        var value = default(CountedArguments);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        ShaderValueSnapshot copy = snapshot.Copy();
        byte[] packed = copy.Data.ToArray();

        Assert.Equal(0, CountedArguments.Probes);
        AssertStaticProvider(snapshot);
        AssertStaticProvider(copy);
        Assert.Equal(value.GetType(), snapshot.DiagnosticType);
        Assert.Empty(snapshot.Fields);
        Assert.Empty(copy.Fields);
        Assert.Equal(snapshot.Data.ToArray(), packed);
        Assert.Equal("Value", Assert.Single(copy.GetDiagnosticFields()).Path);
        Assert.Equal(1, CountedArguments.Probes);
        Assert.Same(copy.GetDiagnosticFields(), snapshot.GetDiagnosticFields());
        Assert.Same(snapshot.DiagnosticFields, IShaderArguments.Capture(in value).DiagnosticFields);
        Assert.Same(ShaderTransfer.Fields(in value), snapshot.GetDiagnosticFields());
        Assert.Same(ShaderTransfer.Fields(typeof(CountedArguments)), snapshot.GetDiagnosticFields());
        Assert.Equal(1, CountedArguments.Probes);
    }

    /// <summary>WGSL packing needs no root schema, even for a fresh target with unreadable reflection.</summary>
    [Fact]
    public void BindingPackingDoesNotParseReflectionOrProbeFields()
    {
        var value = new PlainValue(17);
        using TestRootOwner root = TestShaderTransfer.CreateUnmanaged(in value, "arguments", "PlainValue", size => new TestRootStorage(size));
        var dependencies = ShaderBindingDependencies.Collect(root.Metadata, null);
        var target = new ShaderTargetData(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", "row-major", [32], "not JSON");
        var data = new ShaderBindingData([], target, new DeviceCaps { MaxUniformBuffersPerStage = 1 });

        Assert.Equal(4, dependencies.Arguments.WireSize);
        TestRootStorage storage = Assert.IsType<TestRootStorage>(root.Storage);
        Assert.Equal(17u, System.Runtime.InteropServices.MemoryMarshal.Read<uint>(storage.Bytes));
        Assert.All(storage.Bytes[4..], item => Assert.Equal((byte)0, item));
        Assert.Empty(data.Map);
        Assert.ThrowsAny<JsonException>(() => ShaderDataLayout.Root(target, "arguments"));
    }

    /// <summary>Required root binding discovery does not instantiate optional data or root schema dictionaries.</summary>
    [Fact]
    public void RootDiscoveryDoesNotAllocateDiagnosticSchemaCaches()
    {
        const string Reflection = """
            {"parameters":[{"name":"arguments","type":{"kind":"constantBuffer","elementType":{"kind":"uninspected"}}}]}
            """;
        var target = new ShaderTargetData(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", "row-major", [32], Reflection);
        Assert.True(ShaderDataLayout.HasRoot(target, "arguments"));
        MethodInfo reflection = typeof(ShaderDataLayout).GetMethod("GetReflection", BindingFlags.NonPublic | BindingFlags.Static)!;
        object cache = reflection.Invoke(null, [target])!;
        Assert.Null(cache.GetType().GetField("_roots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache));
        Assert.Null(cache.GetType().GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache));
    }

    /// <summary>Generated numeric offsets and field tables belong only to the explicitly initialized diagnostic holder.</summary>
    [Fact]
    public void GeneratedDiagnosticsHaveNoEagerOuterFieldTable()
    {
        var value = new OptionalGeneratedArguments { Value = 29 };
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        FieldInfo[] fields = typeof(OptionalGeneratedArguments).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Empty(fields);
        Type holder = Assert.Single(typeof(OptionalGeneratedArguments).GetNestedTypes(BindingFlags.NonPublic));
        Assert.False((holder.Attributes & TypeAttributes.BeforeFieldInit) != 0);
        Assert.Empty(snapshot.Fields);
        AssertStaticProvider(snapshot);
        Assert.Equal(typeof(OptionalGeneratedArguments), snapshot.DiagnosticType);
        Assert.Null(typeof(OptionalGeneratedArguments).TypeInitializer);
        ShaderTransferField field = Assert.Single(snapshot.GetDiagnosticFields());
        Assert.Equal("Value", field.Path);
        Assert.Equal(0, field.Offset);
        Assert.Equal(sizeof(uint), field.Size);
        Assert.Same(snapshot.GetDiagnosticFields(), snapshot.GetDiagnosticFields());
    }

    /// <summary>Schema validation evaluates deferred diagnostics only when explicitly requested in either configuration.</summary>
    [Fact]
    public void LayoutValidationProbesOnlyWhenExplicitlyEnabled()
    {
        const string Reflection = """
            {"parameters":[{"name":"arguments","type":{"kind":"constantBuffer","elementType":{
              "kind":"struct","name":"Wire","sizes":[{"kind":"uniform","value":4}],
              "fields":[{"name":"Value","type":{"kind":"scalar","scalarType":"uint32"},"binding":{"offset":0,"size":4}}]
            }}}]}
            """;
        var target = new ShaderTargetData(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", "row-major", [32], Reflection);
        var layout = ShaderDataLayout.Root(target, "arguments");
        int probes = 0;
        var snapshot = new ShaderValueSnapshot("arguments", "Wire", new byte[4], [], [])
        {
            DiagnosticFields = () =>
            {
                probes++;
                return [new("Value", 0, sizeof(uint), typeof(uint))];
            },
        };
        layout.ValidateArguments(snapshot);
        Assert.Equal(0, probes);
        layout.ValidateArguments(snapshot, validateReferences: true);
        Assert.Equal(1, probes);
        Assert.Throws<ArgumentException>(() => layout.ValidateArguments(snapshot with { ShaderTypeName = "Wrong" }, validateReferences: true));
        Assert.Equal(2, probes);
    }

    /// <summary>Every schema-free path rejects dangerous reference slots before calling native encoders.</summary>
    [Fact]
    public void RawPackingRejectsDuplicateReferenceSlotsWithoutDiagnostics()
    {
        var snapshot = new ShaderValueSnapshot(
            "arguments",
            "Wire",
            new byte[16],
            [new(typeof(uint), new object(), 0), new(typeof(uint), new object(), 0)],
            [])
        {
            DiagnosticFields = () => throw new InvalidOperationException("No diagnostic probe expected."),
        };
        Assert.Throws<ArgumentException>(() => ShaderDataLayout.ValidateSnapshot(snapshot));
    }

    /// <summary>Generated struct metadata has no eager initializer and is reached through the deferred static provider.</summary>
    [Fact]
    public void GeneratedManagedStructCaptureRetainsOnlyTypeMetadata()
    {
        var value = new OptionalGeneratedClassArguments { Value = 31 };
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        ShaderValueSnapshot copy = snapshot.Copy();
        AssertStaticProvider(snapshot);
        AssertStaticProvider(copy);
        Assert.Equal(value.GetType(), snapshot.DiagnosticType);
        Assert.Empty(snapshot.Fields);
        Assert.Equal(31u, System.Runtime.InteropServices.MemoryMarshal.Read<uint>(snapshot.Data.Span));

        // With no outer initializer or static fields, capture cannot initialize the
        // diagnostic holder. Its explicit cctor is reachable only from the getters.
        Assert.Null(typeof(OptionalGeneratedClassArguments).TypeInitializer);
        Assert.Empty(typeof(OptionalGeneratedClassArguments).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        Type holder = Assert.Single(typeof(OptionalGeneratedClassArguments).GetNestedTypes(BindingFlags.NonPublic), type => type.Name == "__TransferDiagnostics");
        Assert.False((holder.Attributes & TypeAttributes.BeforeFieldInit) != 0);
        Assert.NotNull(holder.TypeInitializer);
        value.Value = 47;
        ShaderTransferField field = Assert.Single(copy.GetDiagnosticFields());
        Assert.Equal("Value", field.Path);
        Assert.Equal(0, field.Offset);
        Assert.Same(snapshot.GetDiagnosticFields(), copy.GetDiagnosticFields());
        Assert.Equal(31u, System.Runtime.InteropServices.MemoryMarshal.Read<uint>(copy.Data.Span));
    }

    /// <summary>Explicit callbacks remain higher-priority overrides of the normal metadata source.</summary>
    [Fact]
    public void ExplicitCallbackOverridesTypeAndInstanceDiagnostics()
    {
        var value = default(TypeOnlyArguments);
        ShaderValueSnapshot instance = IShaderArguments.Capture(in value);
        var plain = new PlainValue(17);
        ShaderValueSnapshot numeric = ShaderTransfer.Capture(in plain);
        IReadOnlyList<ShaderTransferField> fields = [new("Override", 0, sizeof(uint), typeof(uint))];
        Assert.Same(fields, (instance with { DiagnosticFields = () => fields }).GetDiagnosticFields());
        Assert.Same(fields, (numeric with { DiagnosticFields = () => fields }).GetDiagnosticFields());
    }

    /// <summary>Value-type capture stores its Type and a static generic provider, never a boxed argument.</summary>
    [Fact]
    public void ValueTypeCaptureUsesSharedTypeMetadataWithoutBoxedProvider()
    {
        var value = new TypeOnlyArguments(17);
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        ShaderValueSnapshot copy = snapshot.Copy();
        AssertStaticProvider(snapshot);
        Assert.Equal(typeof(TypeOnlyArguments), snapshot.DiagnosticType);
        Assert.Equal(typeof(TypeOnlyArguments), copy.DiagnosticType);
        Assert.Empty(snapshot.Fields);
        Assert.Equal("Value", Assert.Single(copy.GetDiagnosticFields()).Path);
        Assert.Same(ShaderTransfer.Fields<TypeOnlyArguments>(), snapshot.GetDiagnosticFields());
    }

    /// <summary>Handwritten unmanaged arguments with default metadata retain JIT raw-field diagnostics.</summary>
    [Fact]
    public void UnmanagedArgumentsWithoutStaticFieldsUseRawFallback()
    {
        var value = new NoFieldsArguments { Number = 41 };
        ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
        AssertStaticProvider(snapshot);
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            ShaderTransferField field = Assert.Single(ShaderTransfer.Fields(in value));
            Assert.Equal("Number", field.Path);
            Assert.Equal(0, field.Offset);
            Assert.Equal(sizeof(uint), field.Size);
            Assert.Same(ShaderTransfer.Fields(typeof(NoFieldsArguments)), snapshot.GetDiagnosticFields());
        }
        else
        {
            Assert.Empty(snapshot.GetDiagnosticFields());
        }
    }

    /// <summary>A managed application wrapper without static wire metadata cannot use outer raw offsets.</summary>
    [Fact]
    public void ManagedArgumentsWithoutStaticFieldsDoNotReflectOuterLayout()
    {
        var value = new ManagedNoFieldsArguments { Text = "not wire bytes" };
        Assert.Empty(ShaderTransfer.Fields(in value));
        Assert.Empty(IShaderArguments.Capture(in value).GetDiagnosticFields());
    }

    private static void AssertStaticProvider(ShaderValueSnapshot snapshot)
    {
        Func<IReadOnlyList<ShaderTransferField>> provider = Assert.IsType<Func<IReadOnlyList<ShaderTransferField>>>(snapshot.DiagnosticFields);
        Assert.Null(provider.Target);
        Assert.True(provider.Method.IsStatic);
        Assert.True(provider.Method.IsGenericMethod);
        Assert.Equal("ArgumentFields", provider.Method.Name);
        Assert.Equal(typeof(ShaderTransfer), provider.Method.DeclaringType);
        Assert.Equal(snapshot.DiagnosticType, Assert.Single(provider.Method.GetGenericArguments()));
    }

    private struct NoFieldsArguments : IShaderArguments
    {
        public uint Number;

        public static string ShaderTypeName => "NoFields";

        public static int TransferSize => sizeof(uint);

        public static int ReferenceCount => 0;

        public readonly void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in Number, 1)));
    }

    private struct ManagedNoFieldsArguments : IShaderArguments
    {
        public string Text;

        public static string ShaderTypeName => "ManagedNoFields";

        public static int TransferSize => sizeof(uint);

        public static int ReferenceCount => 0;

        public readonly void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            uint number = (uint)Text.Length;
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in number, 1)));
        }
    }

    private readonly record struct TypeOnlyArguments(uint Value) : IShaderArguments
    {
        public static string ShaderTypeName => "TypeOnly";

        public static int TransferSize => sizeof(uint);

        public static int ReferenceCount => 0;

        public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Value", 0, sizeof(uint), typeof(uint))];

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in this, 1)));
    }

    private readonly record struct PlainValue(uint Value);

    private struct CountedArguments : IShaderArguments
    {
        public static int Probes;

        public static string ShaderTypeName => "Counted";

        public static int TransferSize => sizeof(uint);

        public static int ReferenceCount => 0;

        public static IReadOnlyList<ShaderTransferField> TransferFields
        {
            get
            {
                Interlocked.Increment(ref Probes);
                return [new("Value", 0, sizeof(uint), typeof(uint))];
            }
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            uint value = 17;
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
        }
    }
}
