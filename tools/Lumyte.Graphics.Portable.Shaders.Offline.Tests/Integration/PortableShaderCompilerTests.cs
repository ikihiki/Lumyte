namespace Lumyte.Graphics.Portable.Shaders.Offline.Tests;

[Trait("Category", "ShaderToolchainConformance")]
public sealed class PortableShaderCompilerTests
{
    [TintFact]
    public async Task ReflectsTheImmediateLayoutFromTheFinalWgsl()
    {
        string module = TintTools.Fixture("MixedRoot.wgsl");

        var result = await PortableShaderCompiler.CompileAsync(new(module), TintTools.Options());

        Assert.Equal(module, result.Package.Module);
        var root = Assert.IsType<PortableShaderDataLayout>(result.Package.RootLayout);
        Assert.Equal(("RootData", 32u, 16u), (root.Name, root.Size, root.Alignment));
        Assert.Collection(root.Fields,
            field => Assert.Equal(new PortableShaderFieldLayout("count", "u32", 0, 4, 4), field),
            field => Assert.Equal(new PortableShaderFieldLayout("gain", "f32", 4, 4, 4), field),
            field => Assert.Equal(new PortableShaderFieldLayout("offset", "vec3<f32>", 16, 12, 16), field),
            field => Assert.Equal(new PortableShaderFieldLayout("index", "u32", 28, 4, 4), field));
        Assert.Equal(PortableShaderFeatures.ImmediateAddressSpace, result.Package.RequiredFeatures);
    }

    [TintFact]
    public async Task CompilesAndExecutesTheGeneratedPackageAndResourceInput()
    {
        var result = await PortableShaderCompiler.CompileAsync(new(TintTools.Fixture("MixedRoot.wgsl")), TintTools.Options());

        var assembly = GeneratedConsumer.Compile(result, """
            namespace Generated.Test;
            public static class Consumer
            {
                public static object[] Run()
                {
                    var package = ExamplePackage.Create();
                    var root = new ExampleRootData { count = 3, gain = 2, offset = new ExampleVector3f32 { X = 1, Y = 2, Z = 3 }, index = 7 };
                    return [package, System.Runtime.InteropServices.Marshal.SizeOf<ExampleRootData>(),
                        System.Runtime.InteropServices.Marshal.OffsetOf<ExampleRootData>("offset").ToInt32(),
                        root.offset.Z, ExampleGroup0Resources.Group, ExampleGroup0Resources.AbiHash];
                }
            }
            """);
        var values = Assert.IsType<object[]>(assembly.GetType("Generated.Test.Consumer")!.GetMethod("Run")!.Invoke(null, null));

        var package = Assert.IsType<PortableShaderPackage>(values[0]);
        Assert.Equal(result.Package.Module, package.Module);
        Assert.Equal(new object[] { 32, 16, 3f, 0u, result.Package.AbiHash }, values[1..]);
        Assert.Collection(package.BindingSchema.Entries,
            entry => Assert.Equal(new PortableShaderBindingSchemaEntry("Group0Binding2", 0, 2, GpuBindingLayoutKind.Buffer), entry));
    }

    [TintFact]
    public async Task ReflectsTextureSamplerAndStorageBindings()
    {
        var result = await PortableShaderCompiler.CompileAsync(new(TintTools.Fixture("Bindings.wgsl")), TintTools.Options());

        Assert.Collection(result.Package.GroupLayouts,
            group => Assert.Collection(group.Entries,
                entry => Assert.Equal(new GpuTextureBindingLayout(GpuTextureSampleType.Float), entry.TextureLayout),
                entry => Assert.Equal(new GpuSamplerBindingLayout(GpuSamplerBindingType.Filtering), entry.SamplerLayout)),
            group => Assert.Collection(group.Entries,
                entry => Assert.Equal(new GpuTextureBindingLayout(GpuTextureSampleType.Depth), entry.TextureLayout),
                entry => Assert.Equal(new GpuSamplerBindingLayout(GpuSamplerBindingType.Comparison), entry.SamplerLayout)),
            group => Assert.Collection(group.Entries,
                entry => Assert.Equal(new GpuStorageTextureBindingLayout(GpuStorageTextureAccess.WriteOnly, GpuFormat.Rgba8Unorm), entry.StorageTextureLayout)));
    }

    [TintFact]
    public async Task RejectsASelectorThatIsNotTheImmediateType()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PortableShaderCompiler.CompileAsync(
            new(TintTools.Fixture("MixedRoot.wgsl")), TintTools.Options("OtherRoot")));

        Assert.Contains("differs from the official Tint immediate type 'RootData'", error.Message);
    }

    [TintFact]
    public async Task UsesTheOfficialFrontendForShaderErrors()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PortableShaderCompiler.CompileAsync(
            new("@compute @workgroup_size(1) fn main() { missing(); }"), TintTools.Options()));

        Assert.Contains("Official tint_info failed", error.Message);
        Assert.Contains("missing", error.Message);
    }

    [TintFact]
    public async Task RejectsHostTypesWhoseStrideIsNotAvailable()
    {
        const string module = """
            requires immediate_address_space;
            struct Root { value: mat2x2<f32>, }
            var<immediate> root: Root;
            @group(0) @binding(0) var<storage, read_write> output: f32;
            @compute @workgroup_size(1) fn main() { output = root.value[0][0]; }
            """;

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => PortableShaderCompiler.CompileAsync(new(module), TintTools.Options()));

        Assert.Contains("matrix and array stride reflection", error.Message);
    }

    [TintFact]
    public async Task KeepsTheAbiHashStableForIdenticalBuildInputs()
    {
        var source = new PortableShaderSource(TintTools.Fixture("MixedRoot.wgsl"));

        var first = await PortableShaderCompiler.CompileAsync(source, TintTools.Options());
        var second = await PortableShaderCompiler.CompileAsync(source, TintTools.Options());

        Assert.Equal(first.Package.AbiHash, second.Package.AbiHash);
    }

    [TintFact]
    public async Task DistinguishesAFieldNamedImplicitPaddingFromRealPadding()
    {
        string module = TintTools.Fixture("MixedRoot.wgsl").Replace("count", "implicit_padding", StringComparison.Ordinal);

        var result = await PortableShaderCompiler.CompileAsync(new(module), TintTools.Options());

        var fields = Assert.IsType<PortableShaderDataLayout>(result.Package.RootLayout).Fields;
        Assert.Equal(4, fields.Count);
        Assert.Equal(new PortableShaderFieldLayout("implicit_padding", "u32", 0, 4, 4), fields[0]);
    }

    [TintFact]
    public async Task RejectsHostTypesThatCollideWithThePackageFactory()
    {
        string module = TintTools.Fixture("MixedRoot.wgsl").Replace("RootData", "Package", StringComparison.Ordinal);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PortableShaderCompiler.CompileAsync(new(module), TintTools.Options()));

        Assert.Contains("Generated host type 'ExamplePackage' is duplicated", error.Message);
    }

    [TintFact]
    public async Task RejectsHostTypesThatCollideWithGeneratedResourceInputs()
    {
        string module = TintTools.Fixture("MixedRoot.wgsl").Replace("RootData", "Group0Resources", StringComparison.Ordinal);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PortableShaderCompiler.CompileAsync(new(module), TintTools.Options()));

        Assert.Contains("Generated host type 'ExampleGroup0Resources' is duplicated", error.Message);
    }

    [Fact]
    public async Task RejectsSlangUntilTheDirectRootToolchainIsConnected()
    {
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => PortableShaderCompiler.CompileAsync(
            new("[shader(\"compute\")] void main() {}", language: PortableShaderSourceLanguage.Slang),
            new("unused", "Example", "Example")));

        Assert.Contains("no source rewrite or root buffer fallback", error.Message);
    }
}
