using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises real Mesh/Task compilation and version-one offline artifacts without a GPU.</summary>
public sealed class MeshShaderCompilationTests
{
    private const string MeshSource = """
        struct Vertex { float4 position : SV_Position; float4 color : COLOR0; };
        [shader("mesh")]
        [numthreads(1,1,1)]
        [outputtopology("triangle")]
        void main(out vertices Vertex vertices[3], out indices uint3 triangles[1])
        {
            SetMeshOutputCounts(3,1);
            vertices[0].position=float4(-1,-1,0,1);
            vertices[1].position=float4(0,1,0,1);
            vertices[2].position=float4(1,-1,0,1);
            for(uint i=0;i<3;i++) vertices[i].color=float4(1,0,0,1);
            triangles[0]=uint3(0,1,2);
        }
        """;

    private const string TaskSource = """
        struct Payload { uint value; };
        groupshared Payload payload;
        [shader("amplification")]
        [numthreads(1,1,1)]
        void main() { payload.value=7; DispatchMesh(1,1,1,payload); }
        """;

    /// <summary>Default requests compile exactly SPIR-V and DXIL; explicit requests remain single-target.</summary>
    /// <param name="stage">The public stage, including Task mapped to amplification.</param>
    /// <param name="target">The requested target or stage-specific defaults.</param>
    /// <returns>The asynchronous compilation verification.</returns>
    [Theory]
    [InlineData(ShaderStage.Mesh, null)]
    [InlineData(ShaderStage.Mesh, ShaderTarget.SpirV)]
    [InlineData(ShaderStage.Mesh, ShaderTarget.Dxil)]
    [InlineData(ShaderStage.Task, null)]
    [InlineData(ShaderStage.Task, ShaderTarget.SpirV)]
    [InlineData(ShaderStage.Task, ShaderTarget.Dxil)]
    public async Task MeshStagesProduceRequestedTargetsAsync(ShaderStage stage, ShaderTarget? target)
    {
        var compiler = new SlangShaderCompiler();
        ShaderArtifact artifact = await compiler.CompileAsync(new()
        {
            Source = stage == ShaderStage.Mesh ? MeshSource : TaskSource,
            Stage = stage,
            Target = target,
        });
        Assert.Equal(1u, BitConverter.ToUInt32(artifact.GetBinary(), 4));
        var transported = new ShaderArtifact(artifact.GetBinary());
        ShaderTarget[] expected = target is { } single ? [single] : [ShaderTarget.SpirV, ShaderTarget.Dxil];
        foreach (ShaderTarget current in expected)
        {
            ShaderTargetData data = transported.GetTarget(current);
            Assert.Equal(stage, data.Stage);
            Assert.NotEmpty(data.Code);
            JsonElement metadata = MeshShaderMetadata.Read(data);
            Assert.Equal(new uint[] { 1, 1, 1 }, metadata.GetProperty("workgroupSize").EnumerateArray().Select(value => value.GetUInt32()));
            Assert.Equal(stage == ShaderStage.Mesh ? 8u : 24u, metadata.GetProperty("requiredFeatures").GetUInt32());
            if (stage == ShaderStage.Mesh)
            {
                Assert.Equal(3u, metadata.GetProperty("maxOutputVertices").GetUInt32());
                Assert.Equal(1u, metadata.GetProperty("maxOutputPrimitives").GetUInt32());
                Assert.Equal("triangle", metadata.GetProperty("outputTopology").GetString());
                Assert.Equal(2, metadata.GetProperty("outputs").GetArrayLength());
                Assert.Equal(0UL, metadata.GetProperty("payloadBytes").GetUInt64());
                Assert.Equal(96UL, metadata.GetProperty("outputMemoryBytes").GetUInt64());
            }
            else
            {
                Assert.Equal(4UL, metadata.GetProperty("payloadBytes").GetUInt64());
                Assert.NotEmpty(metadata.GetProperty("payloadAbi").GetString()!);
                Assert.Equal(0UL, metadata.GetProperty("sharedMemoryBytes").GetUInt64());
            }
        }

        Assert.Throws<NotSupportedException>(() => artifact.GetTarget(ShaderTarget.Wgsl));
        if (target is { } selected)
        {
            Assert.Throws<NotSupportedException>(() => artifact.GetTarget(selected == ShaderTarget.SpirV ? ShaderTarget.Dxil : ShaderTarget.SpirV));
        }
    }

    /// <summary>WGSL rejection does not require or start an installed compiler.</summary>
    /// <param name="stage">The unsupported stage.</param>
    /// <returns>The rejection verification.</returns>
    [Theory]
    [InlineData(ShaderStage.Mesh)]
    [InlineData(ShaderStage.Task)]
    public async Task WgslFailsBeforeCompilerStartAsync(ShaderStage stage)
    {
        var compiler = new SlangShaderCompiler("missing-mesh-compiler");
        await Assert.ThrowsAsync<NotSupportedException>(() => compiler.CompileAsync(new() { Source = "void main() {}", Stage = stage, Target = ShaderTarget.Wgsl }));
    }

    /// <summary>Compiler-resolved constants, not literal application-source parsing, supply output counts.</summary>
    /// <returns>The specialized declaration verification.</returns>
    [Fact]
    public async Task CompilerResolvesOutputConstantsAsync()
    {
        string source = "static const uint VertexCount=3;\n" + MeshSource.Replace("vertices[3]", "vertices[VertexCount]", StringComparison.Ordinal);
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Stage = ShaderStage.Mesh, Target = ShaderTarget.SpirV });
        Assert.Equal(3u, MeshShaderMetadata.Read(artifact.GetTarget(ShaderTarget.SpirV)).GetProperty("maxOutputVertices").GetUInt32());
    }

    /// <summary>Offline pack and online compilation use exactly the same reflection completion path.</summary>
    /// <param name="stage">The offline public stage.</param>
    /// <param name="targets">The target set, or omitted metadata.</param>
    /// <returns>The offline build and parity verification.</returns>
    [Theory]
    [InlineData(ShaderStage.Mesh, null)]
    [InlineData(ShaderStage.Task, null)]
    [InlineData(ShaderStage.Mesh, "spirv")]
    [InlineData(ShaderStage.Mesh, "dxil")]
    [InlineData(ShaderStage.Task, "spirv;dxil")]
    public async Task OfflineTargetsMatchOnlineMetadataAsync(ShaderStage stage, string? targets)
    {
        string directory = CreateDirectory();
        try
        {
            string source = stage == ShaderStage.Mesh ? MeshSource : TaskSource;
            string project = await WriteProjectAsync(directory, stage == ShaderStage.Mesh ? "mesh" : "task", targets, source);
            (int exitCode, string diagnostics) = await BuildOfflineAsync(project);
            Assert.True(exitCode == 0, diagnostics);
            using FileStream file = File.OpenRead(Path.Combine(directory, "output", "Slang", "fixture.lshader"));
            var offline = ShaderArtifact.Load(file);
            ShaderArtifact online = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Stage = stage });
            ShaderTarget[] expected = targets == "spirv" ? [ShaderTarget.SpirV] : targets == "dxil" ? [ShaderTarget.Dxil] : [ShaderTarget.SpirV, ShaderTarget.Dxil];
            foreach (ShaderTarget target in expected)
            {
                Assert.Equal(MeshShaderMetadata.Read(online.GetTarget(target)).GetRawText(), MeshShaderMetadata.Read(offline.GetTarget(target)).GetRawText());
                Assert.NotEmpty(offline.GetTarget(target).Code);
            }

            Assert.Throws<NotSupportedException>(() => offline.GetTarget(ShaderTarget.Wgsl));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Invalid target sets are build errors and never produce an artifact.</summary>
    /// <param name="targets">The invalid target metadata.</param>
    /// <returns>The failed build verification.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("spirv;spirv")]
    [InlineData("spirv;unknown")]
    [InlineData("default")]
    [InlineData("wgsl")]
    [InlineData("spirv;")]
    public async Task InvalidOfflineTargetSetsFailAsync(string targets)
    {
        string directory = CreateDirectory();
        try
        {
            string project = await WriteProjectAsync(directory, "mesh", targets, MeshSource);
            (int exitCode, string diagnostics) = await BuildOfflineAsync(project);
            Assert.True(exitCode != 0, diagnostics);
            Assert.False(File.Exists(Path.Combine(directory, "output", "Slang", "fixture.lshader")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A payload-bearing Mesh entry has the same structural wire ABI as its Task producer.</summary>
    /// <param name="target">The target-specific payload layout.</param>
    /// <returns>The paired compilation verification.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Dxil)]
    public async Task TaskAndMeshPayloadAbiMatchesAsync(ShaderTarget target)
    {
        string mesh = "struct Payload { uint value; };\n" + MeshSource
            .Replace("void main(", "void main(in payload Payload data, ", StringComparison.Ordinal)
            .Replace("float4(1,0,0,1)", "float4(data.value,0,0,1)", StringComparison.Ordinal);
        var compiler = new SlangShaderCompiler();
        ShaderArtifact taskArtifact = await compiler.CompileAsync(new() { Source = TaskSource, Stage = ShaderStage.Task, Target = target });
        ShaderArtifact meshArtifact = await compiler.CompileAsync(new() { Source = mesh, Stage = ShaderStage.Mesh, Target = target });
        JsonElement taskMetadata = MeshShaderMetadata.Read(taskArtifact.GetTarget(target));
        JsonElement meshMetadata = MeshShaderMetadata.Read(meshArtifact.GetTarget(target));
        Assert.Equal(taskMetadata.GetProperty("payloadAbi").GetString(), meshMetadata.GetProperty("payloadAbi").GetString());
        Assert.Equal(4UL, meshMetadata.GetProperty("payloadBytes").GetUInt64());
    }

    /// <summary>A failing required target is not silently removed from a default artifact.</summary>
    /// <returns>The all-target failure verification.</returns>
    [Fact]
    public async Task RequiredDxilFailureDoesNotShrinkDefaultTargetsAsync()
    {
        string source = MeshSource + "\n#ifdef LUMYTE_DXIL\n#error RequiredDxilFailure\n#endif\n";
        var compiler = new SlangShaderCompiler();
        ShaderArtifact single = await compiler.CompileAsync(new() { Source = source, Stage = ShaderStage.Mesh, Target = ShaderTarget.SpirV });
        Assert.NotEmpty(single.GetTarget(ShaderTarget.SpirV).Code);
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(new() { Source = source, Stage = ShaderStage.Mesh }));
        Assert.Contains("RequiredDxilFailure", error.Message);
    }

    /// <summary>Existing version-one stages do not acquire Mesh metadata requirements.</summary>
    [Fact]
    public void LegacyVersionOneArtifactStillLoads()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x4448534Cu);
            writer.Write(1u);
            writer.Write(2u);
            writer.Write("main");
            writer.Write("legacy-compiler");
            writer.Write("row-major");
            writer.Write(1u);
            writer.Write(0u);
            byte[] code = Encoding.UTF8.GetBytes("@compute @workgroup_size(1) fn main() {}");
            writer.Write(code.Length);
            writer.Write(code);
            writer.Write("{}");
        }

        var artifact = new ShaderArtifact(stream.ToArray());
        Assert.Equal(ShaderStage.Compute, artifact.GetTarget(ShaderTarget.Wgsl).Stage);
        Assert.Equal("{}", artifact.GetTarget(ShaderTarget.Wgsl).ReflectionJson);
    }

    /// <summary>Locations are read from the native SPIR-V decorations rather than field order.</summary>
    /// <returns>The explicit location verification.</returns>
    [Fact]
    public async Task SpirVPreservesExplicitOutputLocationAsync()
    {
        string source = MeshSource.Replace("float4 color : COLOR0", "[[vk::location(5)]] float4 color : COLOR0", StringComparison.Ordinal);
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Stage = ShaderStage.Mesh, Target = ShaderTarget.SpirV });
        JsonElement metadata = MeshShaderMetadata.Read(artifact.GetTarget(ShaderTarget.SpirV));
        Assert.Contains(metadata.GetProperty("outputs").EnumerateArray(), output => output.GetProperty("location").ValueKind == JsonValueKind.Number && output.GetProperty("location").GetUInt32() == 5);
    }

    /// <summary>Active fixed shared storage contributes to the declared memory requirement.</summary>
    /// <param name="target">The native target.</param>
    /// <returns>The shared memory verification.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Dxil)]
    public async Task SharedMemoryIsNotReportedAsZeroAsync(ShaderTarget target)
    {
        string source = "groupshared uint scratch[4];\n" + MeshSource
            .Replace("SetMeshOutputCounts(3,1);", "scratch[0]=7; SetMeshOutputCounts(3,1);", StringComparison.Ordinal)
            .Replace("float4(1,0,0,1)", "float4(scratch[0],0,0,1)", StringComparison.Ordinal);
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Stage = ShaderStage.Mesh, Target = target });
        Assert.Equal(16UL, MeshShaderMetadata.Read(artifact.GetTarget(target)).GetProperty("sharedMemoryBytes").GetUInt64());
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lumyte-mesh-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task<string> WriteProjectAsync(string directory, string stage, string? targets, string source)
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "tools", "shaders", "Slang.targets")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        string import = SecurityElement.Escape(Path.Combine(root, "tools", "shaders", "Slang.targets"));
        await File.WriteAllTextAsync(Path.Combine(directory, "fixture.slang"), source);
        string project = Path.Combine(directory, "fixture.proj");
        string targetMetadata = targets == null ? string.Empty : $"Targets=\"{SecurityElement.Escape(targets)}\"";
        string content = $"""
            <Project>
              <PropertyGroup><IntermediateOutputPath>{SecurityElement.Escape(Path.Combine(directory, "output"))}/</IntermediateOutputPath></PropertyGroup>
              <ItemGroup><SlangShader Include="fixture.slang" ResourceName="fixture" Stage="{stage}" EntryPoint="main" {targetMetadata} /></ItemGroup>
              <Import Project="{import}" />
            </Project>
            """;
        await File.WriteAllTextAsync(project, content);
        return project;
    }

    private static async Task<(int ExitCode, string Diagnostics)> BuildOfflineAsync(string project)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        foreach (string argument in new[] { "msbuild", project, "-target:CompileSlangShaders", "-verbosity:minimal", "-nologo", "-nr:false", "-m:1" })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        return (process.ExitCode, (await stdout) + (await stderr));
    }
}
