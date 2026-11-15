using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks native root storage without a GPU or backend command writer.</summary>
public sealed class ShaderNativeConstantPreparationTests
{
    /// <summary>All target branches retain their distinct storage contracts.</summary>
    [Fact]
    public void PreparationSeparatesNativeStorageFromWgslUniform()
    {
        foreach (string prefix in new[] { string.Empty, "#include \"lumyte.slang\"\n" })
        {
            string prepared = ShaderSourcePreparation.Prepare(prefix + "struct Root { uint Value; }; ConstantBuffer<Root> arguments;");
            Assert.Contains("#elif defined(LUMYTE_SPIRV)", prepared);
            Assert.Contains("[[vk::push_constant]] ConstantBuffer<", prepared);
            Assert.Contains("register(b8, space0)", prepared);
            Assert.Contains("#else\n[[vk::binding(8, 0)]]", prepared);
        }
    }

    /// <summary>Storage is explicit and old uniform reflection is not upgraded to push constants.</summary>
    /// <param name="target">The compiled target.</param>
    /// <param name="binding">The reflected physical binding kind.</param>
    /// <param name="storage">The expected storage contract.</param>
    [Theory]
    [InlineData(ShaderTarget.SpirV, "pushConstantBuffer", "VkPushConstants")]
    [InlineData(ShaderTarget.SpirV, "descriptorTableSlot", "UniformBuffer")]
    [InlineData(ShaderTarget.Dxil, "constantBuffer", "D3DRoot32BitConstants")]
    [InlineData(ShaderTarget.Wgsl, "descriptorTableSlot", "UniformBuffer")]
    public void ReflectionRecordsStorage(ShaderTarget target, string binding, string storage)
    {
        string raw = "{\"parameters\":[{\"name\":\"arguments\",\"binding\":{\"kind\":\"" + binding + "\"}}]}";
        using var reflection = JsonDocument.Parse(ShaderSourcePreparation.CompleteReflection(raw, string.Empty, target));
        Assert.Equal(storage, reflection.RootElement.GetProperty("lumyteRootStorage").GetString());
        Assert.Equal(1, reflection.RootElement.GetProperty("lumyteAbi").GetInt32());
    }

    /// <summary>Actual compiler reflection keeps exact logical sizes across native boundary sizes.</summary>
    /// <param name="target">The native target.</param>
    /// <param name="words">The logical DWORD count, including over-capacity schemas rejected by the device.</param>
    /// <returns>The completed compiler checks.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV, 1)]
    [InlineData(ShaderTarget.SpirV, 5)]
    [InlineData(ShaderTarget.SpirV, 32)]
    [InlineData(ShaderTarget.SpirV, 33)]
    [InlineData(ShaderTarget.SpirV, 64)]
    [InlineData(ShaderTarget.SpirV, 65)]
    [InlineData(ShaderTarget.Dxil, 58)]
    public async Task CompilerPreservesLogicalNativeRootSizeAsync(ShaderTarget target, int words)
    {
        string fields = string.Join(" ", Enumerable.Range(0, words).Select(i => $"uint V{i};"));
        string sum = string.Join(" + ", Enumerable.Range(0, words).Select(i => $"arguments.V{i}"));
        string source = "#include \"lumyte.slang\"\n" + $"struct Root {{ {fields} }}; ConstantBuffer<Root> arguments; RWByteAddressBuffer output; [numthreads(1,1,1)] void main() {{ output.Store(0, {sum}); }}";
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Target = target });
        using var reflection = JsonDocument.Parse(artifact.GetTarget(target).ReflectionJson);
        Assert.Equal(target == ShaderTarget.SpirV ? "VkPushConstants" : "D3DRoot32BitConstants", reflection.RootElement.GetProperty("lumyteRootStorage").GetString());
        JsonElement root = reflection.RootElement.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "arguments");
        JsonElement logical = root.GetProperty("type").GetProperty("elementType");
        Assert.Equal("Root", logical.GetProperty("name").GetString());
        Assert.Equal(words * 4, logical.GetProperty("sizes").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "uniform").GetProperty("value").GetInt32());
        if (target == ShaderTarget.SpirV)
        {
            Assert.Equal("pushConstantBuffer", root.GetProperty("binding").GetProperty("kind").GetString());
        }
    }
}
