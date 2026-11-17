using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks actual compiler-native varying signatures, rather than invented declaration locations.</summary>
public sealed class MeshLinkageTests
{
    private const string Mesh = """
        struct Vertex { float4 position : SV_Position; nointerpolation uint value : COLOR0; };
        [shader("mesh")] [numthreads(1,1,1)] [outputtopology("triangle")]
        void main(out vertices Vertex vertices[3], out indices uint3 indices[1])
        {
            SetMeshOutputCounts(3,1);
            for(uint i=0;i<3;i++) { vertices[i].position=float4(0,0,0,1); vertices[i].value=7; }
            indices[0]=uint3(0,1,2);
        }
        """;

    /// <summary>Code and memory/payload/signature requirements cannot be edited independently in transported artifacts.</summary>
    /// <param name="target">The real compiled native target.</param>
    /// <returns>The corruption rejection checks.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Dxil)]
    public async Task CompiledRequirementsAreBoundToCodeAsync(ShaderTarget target)
    {
        ShaderTargetData data = (await new SlangShaderCompiler().CompileAsync(new() { Source = Mesh, Stage = ShaderStage.Mesh, Target = target })).GetTarget(target);
        foreach (string field in new[] { "sharedMemoryBytes", "payloadBytes", "outputMemoryBytes" })
        {
            System.Text.Json.Nodes.JsonObject root = System.Text.Json.Nodes.JsonNode.Parse(data.ReflectionJson)!.AsObject();
            root["lumyteMesh"]![field] = 999;
            Assert.Throws<ArgumentException>(() => ShaderArtifact.PackTargets([data with { ReflectionJson = root.ToJsonString() }]));
        }

        System.Text.Json.Nodes.JsonObject signature = System.Text.Json.Nodes.JsonNode.Parse(data.ReflectionJson)!.AsObject();
        signature["lumyteMesh"]!["outputs"]![0]!["type"] = "corrupt-" + signature["lumyteMesh"]!["outputs"]![0]!["type"]!.GetValue<string>();
        Assert.Throws<ArgumentException>(() => ShaderArtifact.PackTargets([data with { ReflectionJson = signature.ToJsonString() }]));
        byte[] corruptCode = (byte[])data.Code.Clone();
        corruptCode[^1] ^= 1;
        Assert.Throws<ArgumentException>(() => ShaderArtifact.PackTargets([data with { Code = corruptCode }]));

        System.Text.Json.Nodes.JsonObject changed = System.Text.Json.Nodes.JsonNode.Parse(data.ReflectionJson)!.AsObject();
        changed["lumyteMesh"]!["workgroupSize"] = new System.Text.Json.Nodes.JsonArray(2, 1, 1);
        string rebound = MeshShaderMetadata.CompleteIntegrity(changed.ToJsonString(), data.Code);
        Assert.Throws<ArgumentException>(() => ShaderArtifact.PackTargets([data with { ReflectionJson = rebound }]));
    }

    /// <summary>Equal root reference slots cannot hide incompatible transitively referenced element structures.</summary>
    /// <returns>The compiler-backed transitive layout rejection.</returns>
    [Fact]
    public async Task TransitiveResourceAbiMismatchIsRejectedAsync()
    {
        const string Root = """
            #include "lumyte.slang"
            struct SharedData { uint value; };
            struct SharedRoot { GpuBufferRef<SharedData> Buffer; };
            ConstantBuffer<SharedRoot> arguments;
            """;
        var compiler = new SlangShaderCompiler();
        string meshSource = Root + "\n" + Mesh.Replace("vertices[i].value=7;", "vertices[i].value=arguments.Buffer.Load(0).value;", StringComparison.Ordinal);
        ShaderTargetData mesh = (await compiler.CompileAsync(new() { Source = meshSource, Stage = ShaderStage.Mesh, Target = ShaderTarget.SpirV })).GetTarget(ShaderTarget.SpirV);
        string fragmentSource = Root.Replace("uint value", "float4 value", StringComparison.Ordinal) + "\nfloat4 main(nointerpolation uint value : COLOR0) : SV_Target { return arguments.Buffer.Load(0).value + value; }";
        ShaderTargetData fragment = (await compiler.CompileAsync(new() { Source = fragmentSource, Stage = ShaderStage.Fragment, Target = ShaderTarget.SpirV })).GetTarget(ShaderTarget.SpirV);
        Assert.Throws<ArgumentException>(() => MeshShaderValidation.Program(mesh, null, fragment));
    }

    /// <summary>DXIL primitive attributes link by native semantic, without invented pixel-input primitive classification.</summary>
    /// <returns>The actual primitive signature compilation and linkage verification.</returns>
    [Fact]
    public async Task DxilPrimitiveAttributeLinksBySemanticAsync()
    {
        const string Source = """
            struct Vertex { float4 position : SV_Position; };
            struct Primitive { nointerpolation uint value : COLOR0; };
            [shader("mesh")] [numthreads(1,1,1)] [outputtopology("triangle")]
            void main(out vertices Vertex vertices[3], out primitives Primitive primitives[1], out indices uint3 indices[1])
            {
                SetMeshOutputCounts(3,1);
                for(uint i=0;i<3;i++) vertices[i].position=float4(0,0,0,1);
                primitives[0].value=7; indices[0]=uint3(0,1,2);
            }
            """;
        var compiler = new SlangShaderCompiler();
        ShaderTargetData mesh = (await compiler.CompileAsync(new() { Source = Source, Stage = ShaderStage.Mesh, Target = ShaderTarget.Dxil })).GetTarget(ShaderTarget.Dxil);
        const string Fragment = "float4 main(nointerpolation uint value : COLOR0) : SV_Target { return float4(value,0,0,1); }";
        ShaderTargetData fragment = (await compiler.CompileAsync(new() { Source = Fragment, Stage = ShaderStage.Fragment, Target = ShaderTarget.Dxil })).GetTarget(ShaderTarget.Dxil);
        MeshShaderValidation.Program(mesh, null, fragment);
        ShaderTargetData mismatch = (await compiler.CompileAsync(new() { Source = Fragment.Replace("COLOR0", "TEXCOORD0", StringComparison.Ordinal), Stage = ShaderStage.Fragment, Target = ShaderTarget.Dxil })).GetTarget(ShaderTarget.Dxil);
        Assert.Throws<ArgumentException>(() => MeshShaderValidation.Program(mesh, null, mismatch));
    }

    /// <summary>WGSL-only Fragment requests do not compile unrelated DXIL branches.</summary>
    /// <returns>The independent target compilation check.</returns>
    [Fact]
    public async Task WgslFragmentDoesNotCompileDxilBranchAsync()
    {
        const string Source = """
            #ifdef LUMYTE_DXIL
            #error UnexpectedDxilBranch
            #endif
            float4 main() : SV_Target { return float4(1,0,0,1); }
            """;
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = Source, Stage = ShaderStage.Fragment, Target = ShaderTarget.Wgsl });
        Assert.NotEmpty(artifact.GetTarget(ShaderTarget.Wgsl).Code);
    }

    /// <summary>Output parameters are not mistaken for Fragment inputs.</summary>
    /// <param name="target">The native target.</param>
    /// <returns>The output-direction compilation check.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Dxil)]
    public async Task FragmentOutputParametersRemainSupportedAsync(ShaderTarget target)
    {
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = "void main(out float4 color : SV_Target0) { color=float4(1,0,0,1); }", Stage = ShaderStage.Fragment, Target = target });
        using var reflection = System.Text.Json.JsonDocument.Parse(artifact.GetTarget(target).ReflectionJson);
        Assert.Equal(0, reflection.RootElement.GetProperty("lumyteFragmentInputs").GetArrayLength());
    }

    /// <summary>Flat integer signatures match and interpolation mismatches fail before native pipeline creation.</summary>
    /// <param name="target">The actual native shader target.</param>
    /// <returns>The compilation and linkage checks.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Dxil)]
    public async Task NativeFlatSignatureMatchesAsync(ShaderTarget target)
    {
        var compiler = new SlangShaderCompiler();
        ShaderTargetData mesh = (await compiler.CompileAsync(new() { Source = Mesh, Stage = ShaderStage.Mesh, Target = target })).GetTarget(target);
        const string Fragment = "float4 main(nointerpolation uint value : COLOR0) : SV_Target { return float4(value,0,0,1); }";
        ShaderTargetData fragment = (await compiler.CompileAsync(new() { Source = Fragment, Stage = ShaderStage.Fragment, Target = target })).GetTarget(target);
        MeshShaderValidation.Program(mesh, null, fragment);
        if (target == ShaderTarget.Dxil)
        {
            ShaderTargetData mismatch = (await compiler.CompileAsync(new() { Source = Fragment.Replace("COLOR0", "TEXCOORD0", StringComparison.Ordinal), Stage = ShaderStage.Fragment, Target = target })).GetTarget(target);
            Assert.Throws<ArgumentException>(() => MeshShaderValidation.Program(mesh, null, mismatch));
        }
    }
}
