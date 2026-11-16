using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Array varyings retain all signature elements in conventional DXIL compilation.</summary>
public sealed class FragmentArraySignatureTests
{
    /// <summary>Every array element participates in Mesh to Fragment semantic linkage.</summary>
    /// <returns>The compiler-backed positive and missing-second-element checks.</returns>
    [Fact]
    public async Task MeshArrayOutputsLinkEveryElementAsync()
    {
        const string Mesh = """
            struct Vertex { float4 position : SV_Position; nointerpolation float4 colors[2] : COLOR3; };
            [shader("mesh")] [numthreads(1,1,1)] [outputtopology("triangle")]
            void main(out vertices Vertex vertices[3], out indices uint3 triangles[1])
            {
                SetMeshOutputCounts(3,1);
                for(uint i=0;i<3;i++)
                { vertices[i].position=float4(0,0,0,1); vertices[i].colors[0]=float4(1,0,0,1); vertices[i].colors[1]=float4(0,1,0,1); }
                triangles[0]=uint3(0,1,2);
            }
            """;
        const string Fragment = "[shader(\"fragment\")] float4 main(nointerpolation float4 colors[2] : COLOR3) : SV_Target { return colors[0] + colors[1]; }";
        var compiler = new SlangShaderCompiler();
        ShaderTargetData mesh = (await compiler.CompileAsync(new() { Source = Mesh, Stage = ShaderStage.Mesh, Target = ShaderTarget.Dxil })).GetTarget(ShaderTarget.Dxil);
        ShaderTargetData fragment = (await compiler.CompileAsync(new() { Source = Fragment, Stage = ShaderStage.Fragment, Target = ShaderTarget.Dxil })).GetTarget(ShaderTarget.Dxil);
        MeshShaderValidation.Program(mesh, null, fragment);
        ShaderTargetData mismatch = (await compiler.CompileAsync(new() { Source = Fragment.Replace("COLOR3", "COLOR4", StringComparison.Ordinal), Stage = ShaderStage.Fragment, Target = ShaderTarget.Dxil })).GetTarget(ShaderTarget.Dxil);
        Assert.Throws<ArgumentException>(() => MeshShaderValidation.Program(mesh, null, mismatch));
    }

    /// <summary>Both struct fields and entry parameters preserve array semantics and interpolation.</summary>
    /// <param name="structure">Whether the entry takes a structure or a direct array.</param>
    /// <returns>The real compiler-backed signature verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConventionalFragmentArrayInputsCompileAsync(bool structure)
    {
        string source = structure
            ? "struct Input { nointerpolation float4 colors[2] : COLOR3; }; [shader(\"fragment\")] float4 main(Input input) : SV_Target { return input.colors[0] + input.colors[1]; }"
            : "[shader(\"fragment\")] float4 main(nointerpolation float4 colors[2] : COLOR3) : SV_Target { return colors[0] + colors[1]; }";
        ShaderTargetData data = (await new SlangShaderCompiler().CompileAsync(new() { Source = source, Stage = ShaderStage.Fragment, Target = ShaderTarget.Dxil })).GetTarget(ShaderTarget.Dxil);
        Assert.NotEmpty(data.Code);
        using var reflection = JsonDocument.Parse(data.ReflectionJson);
        JsonElement[] inputs = reflection.RootElement.GetProperty("lumyteFragmentInputs").EnumerateArray().ToArray();
        Assert.Equal(new[] { "COLOR3", "COLOR4" }, inputs.Select(value => value.GetProperty("semantic").GetString()));
        Assert.All(inputs, value =>
        {
            Assert.Equal("float4", value.GetProperty("type").GetString());
            Assert.Equal("nointerpolation", value.GetProperty("interpolation").GetString());
            Assert.False(value.GetProperty("perPrimitive").GetBoolean());
        });
    }
}
