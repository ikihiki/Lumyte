using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lumyte.Graphics.Native.Shaders.Offline.Tests;

[Trait("Category", "SlangConformance")]
public sealed class NativeShaderCompilerTests : IClassFixture<NativeCompilerFixture>
{
    private readonly NativeShaderBuildResult result;
    private readonly NativeCompilerFixture fixture;
    public NativeShaderCompilerTests(NativeCompilerFixture fixture) { this.fixture = fixture; result = fixture.Result; }

    [Fact]
    public async Task RepeatedCompilationPreservesArtifactAndInputAbiIdentity()
    {
        NativeShaderBuildResult rebuilt = await fixture.BuildAsync();

        Assert.Equal(result.Package.Artifacts.Select(a => a.AbiHash), rebuilt.Package.Artifacts.Select(a => a.AbiHash));
        Assert.Equal(result.ResourceInputFiles.Keys, rebuilt.ResourceInputFiles.Keys);
    }

    [Fact]
    public async Task ParameterTypeCannotCollideWithItsByteStorageHelper()
    {
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.BuildWithParameterTypeAsync("Bytes16"));

        Assert.Contains("Input type 'Bytes16' collides", error.Message);
    }

    [Fact]
    public void TargetReflectionDeterminesDistinctRootLayouts()
    {
        Assert.Collection(result.Package.Artifacts,
            dx => Assert.Equal((32u, 20u), (dx.RootLayout.Size, dx.RootLayout.Fields.Single(f => f.Name == "colour").Offset)),
            vk => Assert.Equal((48u, 32u), (vk.RootLayout.Size, vk.RootLayout.Fields.Single(f => f.Name == "colour").Offset)));
    }

    [Fact]
    public void ResourceInputsUseTheArtifactAbiAndReflectedOffsets()
    {
        foreach (NativeShaderArtifact artifact in result.Package.Artifacts)
        {
            XElement input = XElement.Parse(result.ResourceInputFiles[artifact.Target + ".Root.native.resources.xml"]);

            Assert.Equal(artifact.AbiHash, (string?)input.Attribute("abiHash"));
            Assert.Collection(input.Elements("field"),
                address => Assert.Equal(("address", "GpuAddress", "0"), ((string?)address.Attribute("name"), (string?)address.Attribute("kind"), (string?)address.Attribute("offset"))),
                view => Assert.Equal(("output", "View", "12"), ((string?)view.Attribute("name"), (string?)view.Attribute("resource"), (string?)view.Attribute("offset"))),
                sampler => Assert.Equal(("sampler", "Sampler", "16"), ((string?)sampler.Attribute("name"), (string?)sampler.Attribute("resource"), (string?)sampler.Attribute("offset"))));
        }
    }

    [Fact]
    public void ParameterTypeProbePreservesTheShaderResourceSemantics()
    {
        NativeShaderInputLayout parameter = Assert.Single(result.Package.Artifacts[1].ParameterLayouts);

        Assert.Equal("Parameters", parameter.AbiId);
        Assert.Equal(NativeShaderResourceKind.View, parameter.Fields.Single(f => f.Name == "albedo").ResourceKind);
        Assert.Equal(NativeShaderResourceKind.Sampler, parameter.Fields.Single(f => f.Name == "sampler").ResourceKind);
        Assert.Collection(parameter.Fields.Where(f => f.Name.StartsWith("values_", StringComparison.Ordinal)),
            first => Assert.Equal("values_0", first.Name), second => Assert.Equal(firstOffset(parameter) + 4, second.Offset),
            third => Assert.Equal(firstOffset(parameter) + 8, third.Offset));

        static uint firstOffset(NativeShaderInputLayout layout) => layout.Fields.Single(f => f.Name == "values_0").Offset;
    }

    [Fact]
    public void GeneratedHostTypesAndPackageFactoryCompileAndExecute()
    {
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(NativeShaderPackage).Assembly.Location, typeof(NativeGpuRange).Assembly.Location, typeof(GpuShaderStage).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("NativeGenerated_" + Guid.NewGuid().ToString("N"),
            result.HostSourceFiles.Values.Select(source => CSharpSyntaxTree.ParseText(source)), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        Assembly assembly = Assembly.Load(stream.ToArray());
        Type dx = assembly.GetType("CompilerConsumer.DirectX12.Root")!;
        Type vk = assembly.GetType("CompilerConsumer.Vulkan.Root")!;
        NativeShaderPackage package = (NativeShaderPackage)assembly.GetType("CompilerConsumer.ShaderPackage")!.GetMethod("Create")!.Invoke(null, null)!;

        Assert.Equal((32, 48), (Marshal.SizeOf(dx), Marshal.SizeOf(vk)));
        Assert.Equal((20L, 32L), (Marshal.OffsetOf(dx, "colour").ToInt64(), Marshal.OffsetOf(vk, "colour").ToInt64()));
        Assert.Equal(result.Package.Artifacts.Select(a => a.AbiHash), package.Artifacts.Select(a => a.AbiHash));
    }
}

public sealed class NativeCompilerFixture
{
    private readonly NativeShaderCompiler compiler;
    private readonly NativeShaderBuildRequest request;

    public NativeCompilerFixture()
    {
        string repository = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repository, "Lumyte.slnx"))) { repository = Directory.GetParent(repository)?.FullName ?? throw new DirectoryNotFoundException("Repository not found."); }
        string compiler = Environment.GetEnvironmentVariable("LUMYTE_SLANGC") ?? Path.Combine(repository, "artifacts/experiments/slang-wgsl-root/compiler/slang-2026.17/bin/slangc.exe");
        string downstream = Environment.GetEnvironmentVariable("LUMYTE_DXC_DIRECTORY") ?? AppContext.BaseDirectory;
        if (!File.Exists(compiler)) { throw new FileNotFoundException("Slang conformance requires LUMYTE_SLANGC pointing to Slang 2026.17.", compiler); }
        this.compiler = new NativeShaderCompiler(compiler, downstream);
        request = new(Path.Combine(AppContext.BaseDirectory, "Fixtures/Resources.slang"),
            [new("main", GpuShaderStage.Compute)], [new(NativeShaderTarget.DirectX12), new(NativeShaderTarget.Vulkan)],
            "CompilerConsumer", parameterTypes: ["Parameters"]);
        Result = BuildAsync().GetAwaiter().GetResult();
    }

    public NativeShaderBuildResult Result { get; }
    public Task<NativeShaderBuildResult> BuildAsync() => compiler.BuildAsync(request);
    public Task<NativeShaderBuildResult> BuildWithParameterTypeAsync(string parameterType)
        => compiler.BuildAsync(new(request.SourcePath, request.EntryPoints, request.Targets,
            request.HostNamespace, parameterTypes: [parameterType]));
}
