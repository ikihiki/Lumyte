using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Checks native signature admission using compiled DXIL without a Windows driver.</summary>
public sealed class PackedMeshSignatureTests
{
    /// <summary>More than 32 scalar declarations still fit the packed native signature.</summary>
    /// <returns>The asynchronous compiler-backed verification.</returns>
    [Fact]
    public async Task PackedScalarOutputsAreAdmittedAsync()
    {
        var source = new StringBuilder("struct Vertex { float4 position : SV_Position;");
        for (int i = 0; i < 33; i++)
        {
            source.Append($"float f{i} : TEXCOORD{i};");
        }

        source.Append("}; [shader(\"mesh\")][numthreads(1,1,1)][outputtopology(\"triangle\")] void main(out vertices Vertex vertices[3], out indices uint3 triangles[1]) { SetMeshOutputCounts(3,1); vertices[0].position=float4(0,0,0,1);");
        for (int i = 0; i < 33; i++)
        {
            source.Append($"vertices[0].f{i}={i};");
        }

        source.Append("triangles[0]=uint3(0,1,2); }");
        ShaderTargetData mesh = (await new SlangShaderCompiler().CompileAsync(new() { Source = source.ToString(), Stage = ShaderStage.Mesh, Target = ShaderTarget.Dxil })).GetTarget(ShaderTarget.Dxil);
        JsonElement metadata = Lumyte.Graphics.Shared.MeshShaderValidation.Read(mesh);
        Assert.Equal(34, metadata.GetProperty("outputs").GetArrayLength());
        Assert.Equal(10UL, Direct3D12MeshPipeline.ReadOutputSignatureRows(mesh.Code));
        Direct3D12MeshPipeline.ValidateNativeOutput(metadata, mesh.Code);
    }

    /// <summary>Primitive scalars share their own row and culling has no allocated register.</summary>
    /// <returns>The asynchronous compiler-backed verification.</returns>
    [Fact]
    public async Task PrimitiveSignaturePackingAndCullAreReadAsync()
    {
        const string Source = """
            struct V { float4 p : SV_Position; };
            struct P { float a:TEXCOORD0; float b:TEXCOORD1; bool c:SV_CullPrimitive; };
            [shader("mesh")][numthreads(1,1,1)][outputtopology("triangle")]
            void main(out vertices V v[3], out indices uint3 t[1], out primitives P p[1])
            { SetMeshOutputCounts(3,1); v[0].p=0; t[0]=uint3(0,1,2); p[0].a=1; p[0].b=2; p[0].c=false; }
            """;
        ShaderTargetData mesh = (await new SlangShaderCompiler().CompileAsync(new() { Source = Source, Stage = ShaderStage.Mesh, Target = ShaderTarget.Dxil })).GetTarget(ShaderTarget.Dxil);
        Assert.Equal(2UL, Direct3D12MeshPipeline.ReadOutputSignatureRows(mesh.Code));
        Direct3D12MeshPipeline.ValidateNativeOutput(Lumyte.Graphics.Shared.MeshShaderValidation.Read(mesh), mesh.Code);
    }

    /// <summary>The combined highest allocated vertex and primitive rows enforce the exact boundary.</summary>
    [Fact]
    public void CombinedPackedRowsHaveExactBoundary()
    {
        using var metadata = JsonDocument.Parse("""
            { "maxOutputVertices": 3, "maxOutputPrimitives": 1, "payloadBytes": 0, "outputs": [] }
            """);
        byte[] code = Container(30, 0);
        Assert.Equal(32UL, Direct3D12MeshPipeline.ReadOutputSignatureRows(code));
        Direct3D12MeshPipeline.ValidateNativeOutput(metadata.RootElement, code);
        code = Container(31, 0);
        Assert.Equal(33UL, Direct3D12MeshPipeline.ReadOutputSignatureRows(code));
        Assert.Throws<NotSupportedException>(() => Direct3D12MeshPipeline.ValidateNativeOutput(metadata.RootElement, code));
    }

    /// <summary>Truncated, absent, duplicate and corrupt signature tables fail closed.</summary>
    [Fact]
    public void MalformedSignaturesAreRejected()
    {
        byte[] code = Container(0, 0);
        Assert.Throws<ArgumentException>(() => Direct3D12MeshPipeline.ReadOutputSignatureRows(code.AsSpan(0, code.Length - 1)));
        Write(code, 32, uint.MaxValue);
        Assert.Throws<ArgumentException>(() => Direct3D12MeshPipeline.ReadOutputSignatureRows(code));
        code = Container(0, 0);
        Write(code, 52, uint.MaxValue);
        Assert.Throws<ArgumentException>(() => Direct3D12MeshPipeline.ReadOutputSignatureRows(code));
        code = Container(0, 0);
        "OSG1"u8.CopyTo(code.AsSpan(88));
        Assert.Throws<ArgumentException>(() => Direct3D12MeshPipeline.ReadOutputSignatureRows(code));
        code = Container(0, 0);
        "ISG1"u8.CopyTo(code.AsSpan(40));
        Assert.Throws<ArgumentException>(() => Direct3D12MeshPipeline.ReadOutputSignatureRows(code));
        code = Container(0, 0);
        Write(code, 56, uint.MaxValue);
        Assert.Throws<ArgumentException>(() => Direct3D12MeshPipeline.ReadOutputSignatureRows(code));
        code = Container(0, 0);
        Write(code, 44, uint.MaxValue);
        Assert.Throws<ArgumentException>(() => Direct3D12MeshPipeline.ReadOutputSignatureRows(code));
        code = Container(0, 0);
        code[80] = 0x10;
        Assert.Throws<ArgumentException>(() => Direct3D12MeshPipeline.ReadOutputSignatureRows(code));
    }

    /// <summary>Native output and payload budgets remain independent of packed signature size.</summary>
    /// <param name="vertices">The declared vertex array size.</param>
    /// <param name="payload">The payload bytes.</param>
    /// <param name="valid">Whether the rounded storage fits.</param>
    [Theory]
    [InlineData(256, 0, true)]
    [InlineData(256, 15360, true)]
    [InlineData(256, 15361, false)]
    [InlineData(257, 0, false)]
    [InlineData(1, 48129, false)]
    public void OutputStorageBudgetsRemainChecked(int vertices, int payload, bool valid)
    {
        object[] outputs = Enumerable.Range(0, 8).Select(_ => (object)new { type = "float4", perPrimitive = false }).ToArray();
        using var metadata = JsonDocument.Parse(JsonSerializer.Serialize(new { maxOutputVertices = vertices, maxOutputPrimitives = 1, payloadBytes = payload, outputs }));
        byte[] code = Container(0, 0);
        if (valid)
        {
            Direct3D12MeshPipeline.ValidateNativeOutput(metadata.RootElement, code);
        }
        else
        {
            Assert.Throws<NotSupportedException>(() => Direct3D12MeshPipeline.ValidateNativeOutput(metadata.RootElement, code));
        }
    }

    private static byte[] Container(uint vertexRegister, uint primitiveRegister)
    {
        // Minimal serialized OSG1/PSG1 fixtures isolate allocation and bounds validation.
        byte[] code = new byte[136];
        "DXBC"u8.CopyTo(code);
        Write(code, 20, 1);
        Write(code, 24, (uint)code.Length);
        Write(code, 28, 2);
        Write(code, 32, 40);
        Write(code, 36, 88);
        for (int i = 0; i < 2; i++)
        {
            int part = 40 + (i * 48);
            (i == 0 ? "OSG1"u8 : "PSG1"u8).CopyTo(code.AsSpan(part));
            Write(code, part + 4, 40);
            Write(code, part + 8, 1);
            Write(code, part + 12, 8);
            Write(code, part + 36, i == 0 ? vertexRegister : primitiveRegister);
            code[part + 40] = 1;
        }

        return code;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}
