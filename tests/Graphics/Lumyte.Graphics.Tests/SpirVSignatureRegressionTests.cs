using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
using Lumyte.Graphics.Vulkan;
using Silk.NET.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Real SPIR-V arrays and matrices retain every native occupied location.</summary>
public sealed class SpirVSignatureRegressionTests
{
    private const string Mesh = """
        struct Vertex { float4 position : SV_Position; [[vk::location(3)]] nointerpolation float4 colors[2] : COLOR0; };
        [shader("mesh")] [numthreads(1,1,1)] [outputtopology("triangle")]
        void main(out vertices Vertex vertices[3], out indices uint3 triangles[1])
        {
            SetMeshOutputCounts(3,1);
            for(uint i=0;i<3;i++) { vertices[i].position=float4(0,0,0,1); vertices[i].colors[0]=float4(1,0,0,1); vertices[i].colors[1]=float4(0,1,0,1); }
            triangles[0]=uint3(0,1,2);
        }
        """;

    /// <summary>A single vector must not satisfy an array input at the same starting location.</summary>
    /// <returns>The real compiler and public program validation checks.</returns>
    [Fact]
    public async Task MissingSecondArrayElementIsRejectedAsync()
    {
        ShaderTargetData fragment = await CompileAsync("float4 main([[vk::location(3)]] nointerpolation float4 colors[2] : COLOR0) : SV_Target { return colors[0]+colors[1]; }", ShaderStage.Fragment);
        ShaderTargetData single = await CompileAsync(Mesh.Replace("colors[2]", "colors").Replace("vertices[i].colors[0]", "vertices[i].colors").Replace("vertices[i].colors[1]=float4(0,1,0,1);", string.Empty), ShaderStage.Mesh);
        Assert.Throws<ArgumentException>(() => MeshShaderValidation.Program(single, null, fragment));
        ShaderTargetData matching = await CompileAsync(Mesh, ShaderStage.Mesh);
        MeshShaderValidation.Program(matching, null, fragment);
        AssertSignature(Inputs(fragment), 3, 2, "float4", "nointerpolation");
        AssertSignature(MeshShaderMetadata.Read(matching).GetProperty("outputs").EnumerateArray().Where(v => v.GetProperty("location").ValueKind == JsonValueKind.Number).ToArray(), 3, 2, "float4", "nointerpolation");
        ShaderTargetData shifted = await CompileAsync("float4 main([[vk::location(4)]] nointerpolation float4 colors[2] : COLOR0) : SV_Target { return colors[0]+colors[1]; }", ShaderStage.Fragment);
        Assert.Throws<ArgumentException>(() => MeshShaderValidation.Program(matching, null, shifted));
    }

    /// <summary>The actual Vulkan validator charges both varying elements at rounded vertex granularity.</summary>
    /// <returns>The native storage and component boundary checks without creating a GPU.</returns>
    [Fact]
    public async Task ArrayOutputStorageUsesEveryLocationAsync()
    {
        ShaderTargetData mesh = await CompileAsync(Mesh, ShaderStage.Mesh);

        // Position plus two float4 locations: 3 * 16 * round(3,4) = 192 bytes.
        ValidateVulkan(mesh, 192, 12);
        Assert.IsType<NotSupportedException>(Assert.Throws<TargetInvocationException>(() => ValidateVulkan(mesh, 191, 12)).InnerException);
        Assert.IsType<NotSupportedException>(Assert.Throws<TargetInvocationException>(() => ValidateVulkan(mesh, 192, 11)).InnerException);
    }

    /// <summary>Conventional fragment matrices, including matrix arrays, compile through the public API.</summary>
    /// <param name="array">Whether the varying is an array of two matrices.</param>
    /// <param name="rows">The declared matrix row count.</param>
    /// <param name="columns">The declared matrix column count.</param>
    /// <returns>The occupied native vector location checks.</returns>
    [Theory]
    [InlineData(false, 3, 3)]
    [InlineData(true, 3, 3)]
    [InlineData(false, 2, 3)]
    [InlineData(true, 2, 3)]
    [InlineData(false, 3, 2)]
    [InlineData(true, 3, 2)]
    public async Task ConventionalMatrixInputsCompileAsync(bool array, int rows, int columns)
    {
        string source = array
            ? $"float4 main([[vk::location(5)]] noperspective float{rows}x{columns} basis[2] : TEXCOORD0, float4 position : SV_Position) : SV_Target {{ return float4(basis[0][0][0]+basis[1][{rows - 1}][{columns - 1}],0,0,position.x); }}"
            : $"float4 main([[vk::location(5)]] noperspective float{rows}x{columns} basis : TEXCOORD0, float4 position : SV_Position) : SV_Target {{ return float4(basis[0][0]+basis[{rows - 1}][{columns - 1}],0,0,position.x); }}";
        ShaderTargetData fragment = await CompileAsync(source, ShaderStage.Fragment);
        Assert.NotEmpty(fragment.Code);
        AssertSignature(Inputs(fragment), 5, rows * (array ? 2 : 1), "float" + columns, "noperspective");
        Assert.NotEmpty(new ShaderArtifact(ShaderArtifact.PackTargets([fragment]).GetBinary()).GetTarget(ShaderTarget.SpirV).Code);
    }

    /// <summary>Matrix columns link individually and retain interpolation and native scalar types.</summary>
    /// <returns>The matrix producer/consumer and mismatched interpolation checks.</returns>
    [Fact]
    public async Task MatrixColumnsLinkEveryLocationAsync()
    {
        string meshSource = Mesh.Replace("float4 colors[2]", "float3 colors[3]", StringComparison.Ordinal)
            .Replace("vertices[i].colors[0]=float4(1,0,0,1); vertices[i].colors[1]=float4(0,1,0,1);", "vertices[i].colors[0]=float3(1,2,3); vertices[i].colors[1]=float3(4,5,6); vertices[i].colors[2]=float3(7,8,9);", StringComparison.Ordinal);
        const string Fragment = "float4 main([[vk::location(3)]] nointerpolation float3x3 colors : COLOR0) : SV_Target { return float4(colors[0]+colors[2],1); }";
        ShaderTargetData mesh = await CompileAsync(meshSource, ShaderStage.Mesh);
        ShaderTargetData fragment = await CompileAsync(Fragment, ShaderStage.Fragment);
        MeshShaderValidation.Program(mesh, null, fragment);
        ShaderTargetData mismatch = await CompileAsync(Fragment.Replace("nointerpolation", "noperspective", StringComparison.Ordinal), ShaderStage.Fragment);
        Assert.Throws<ArgumentException>(() => MeshShaderValidation.Program(mesh, null, mismatch));
        AssertSignature(Inputs(fragment), 3, 3, "float3", "nointerpolation");
    }

    /// <summary>Per-primitive arrays retain their classification across all expanded locations.</summary>
    /// <returns>The native compiler-backed primitive signature check.</returns>
    [Fact]
    public async Task PrimitiveArrayOutputsRetainClassificationAsync()
    {
        const string Source = """
            struct Vertex { float4 position : SV_Position; };
            struct Primitive { [[vk::location(7)]] nointerpolation uint values[2] : COLOR0; };
            [shader("mesh")] [numthreads(1,1,1)] [outputtopology("triangle")]
            void main(out vertices Vertex vertices[3], out primitives Primitive primitives[1], out indices uint3 triangles[1])
            {
                SetMeshOutputCounts(3,1);
                for(uint i=0;i<3;i++) vertices[i].position=float4(0,0,0,1);
                primitives[0].values[0]=1; primitives[0].values[1]=2; triangles[0]=uint3(0,1,2);
            }
            """;
        ShaderTargetData mesh = await CompileAsync(Source, ShaderStage.Mesh);
        JsonElement[] outputs = MeshShaderMetadata.Read(mesh).GetProperty("outputs").EnumerateArray().Where(v => v.GetProperty("location").ValueKind == JsonValueKind.Number).ToArray();
        Assert.Equal(new uint[] { 7, 8 }, outputs.Select(v => v.GetProperty("location").GetUInt32()));

        // This Slang version omits PerPrimitiveEXT on user primitive attributes.
        // Reflection must retain native classification, not invent it from source.
        Assert.All(outputs, v =>
        {
            Assert.Equal("uint", v.GetProperty("type").GetString());
            Assert.Equal("nointerpolation", v.GetProperty("interpolation").GetString());
            Assert.False(v.GetProperty("perPrimitive").GetBoolean());
        });
    }

    /// <summary>Malformed native array lengths, type cycles and truncated instructions fail closed.</summary>
    /// <returns>The compiler-generated binary mutation checks.</returns>
    [Fact]
    public async Task MalformedArraySignaturesAreRejectedAsync()
    {
        ShaderTargetData fragment = await CompileAsync("float4 main([[vk::location(3)]] float4 colors[2] : COLOR0) : SV_Target { return colors[0]+colors[1]; }", ShaderStage.Fragment);
        Type reflection = typeof(SlangShaderCompiler).Assembly.GetType("Lumyte.Graphics.Shaders.MeshSpirVReflection", true)!;
        MethodInfo inputs = reflection.GetMethod("Inputs", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (bool cycle in new[] { false, true })
        {
            byte[] code = (byte[])fragment.Code.Clone();
            for (int offset = 20; offset < code.Length;)
            {
                uint header = BitConverter.ToUInt32(code, offset);
                if ((header & 65535) == 28)
                {
                    uint id = BitConverter.ToUInt32(code, offset + 4);
                    BitConverter.GetBytes(cycle ? id : uint.MaxValue).CopyTo(code, offset + (cycle ? 8 : 12));
                    break;
                }

                offset += checked((int)(header >> 16) * 4);
            }

            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => inputs.Invoke(null, [code])).InnerException);
        }

        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => inputs.Invoke(null, [fragment.Code[..^1]])).InnerException);
    }

    /// <summary>OpTypeMatrix expands native columns even on compilers that use struct wrappers.</summary>
    [Fact]
    public void NativeMatrixOpcodeExpandsColumns()
    {
        uint[] words =
        [
            0x07230203, 0x00010500, 0, 8, 0,
            (3u << 16) | 22, 1, 32,
            (4u << 16) | 23, 2, 1, 3,
            (4u << 16) | 24, 3, 2, 2,
            (4u << 16) | 32, 4, 1, 3,
            (4u << 16) | 71, 5, 30, 6,
            (4u << 16) | 59, 4, 5, 1,
        ];
        byte[] code = words.SelectMany(BitConverter.GetBytes).ToArray();
        Type reflection = typeof(SlangShaderCompiler).Assembly.GetType("Lumyte.Graphics.Shaders.MeshSpirVReflection", true)!;
        var inputs = (System.Text.Json.Nodes.JsonArray)reflection.GetMethod("Inputs", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [code])!;
        using var doc = JsonDocument.Parse(inputs.ToJsonString());
        AssertSignature(doc.RootElement.EnumerateArray().ToArray(), 6, 2, "float3", "linear");
        uint[] decorated = [.. words, (3u << 16) | 71, 5, 5271, (3u << 16) | 71, 5, 14];
        byte[] primitiveCode = decorated.SelectMany(BitConverter.GetBytes).ToArray();
        var primitive = (System.Text.Json.Nodes.JsonArray)reflection.GetMethod("Inputs", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [primitiveCode])!;
        Assert.All(primitive, value =>
        {
            Assert.True(value!["perPrimitive"]!.GetValue<bool>());
            Assert.Equal("nointerpolation", value["interpolation"]!.GetValue<string>());
        });
    }

    private static async Task<ShaderTargetData> CompileAsync(string source, ShaderStage stage) =>
        (await new SlangShaderCompiler().CompileAsync(new() { Source = source, Stage = stage, Target = ShaderTarget.SpirV })).GetTarget(ShaderTarget.SpirV);

    private static JsonElement[] Inputs(ShaderTargetData data)
    {
        using var doc = JsonDocument.Parse(data.ReflectionJson);
        return doc.RootElement.GetProperty("lumyteFragmentInputs").EnumerateArray().Select(v => v.Clone()).ToArray();
    }

    private static void AssertSignature(JsonElement[] values, uint location, int count, string type, string interpolation)
    {
        Assert.Equal(Enumerable.Range(0, count).Select(i => location + (uint)i), values.Select(v => v.GetProperty("location").GetUInt32()));
        Assert.All(values, v =>
        {
            Assert.Equal(type, v.GetProperty("type").GetString());
            Assert.Equal(interpolation, v.GetProperty("interpolation").GetString());
            Assert.False(v.GetProperty("perPrimitive").GetBoolean());
        });
    }

    private static void ValidateVulkan(ShaderTargetData mesh, uint bytes, uint components)
    {
        var device = (VulkanDevice)RuntimeHelpers.GetUninitializedObject(typeof(VulkanDevice));

        // Only managed capability validation is invoked; no device methods or native handles are used.
        typeof(VulkanDevice).GetField("<Caps>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(device, new DeviceCaps
        {
            ShaderTarget = ShaderTarget.SpirV,
            Features = GraphicsFeatures.MeshShader,
            MeshShaderLimits = new() { Workgroup = new() { MaxSizeX = 1, MaxSizeY = 1, MaxSizeZ = 1, MaxInvocations = 1 }, MaxOutputVertices = 3, MaxOutputPrimitives = 1, MaxOutputMemoryBytes = 1024, MaxPayloadAndOutputMemoryBytes = 1024 },
        });
        typeof(VulkanDevice).GetProperty("MeshProperties", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(device, new PhysicalDeviceMeshShaderPropertiesEXT
        {
            MeshOutputPerVertexGranularity = 4,
            MeshOutputPerPrimitiveGranularity = 1,
            MaxMeshOutputComponents = components,
            MaxMeshOutputMemorySize = bytes,
            MaxMeshPayloadAndOutputMemorySize = bytes,
        });
        Type validation = typeof(VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanMeshValidation", true)!;
        validation.GetMethod("Program", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [device, new MeshPipelineDesc { MeshShader = null! }, mesh, null, null]);
        GC.SuppressFinalize(device);
    }
}
