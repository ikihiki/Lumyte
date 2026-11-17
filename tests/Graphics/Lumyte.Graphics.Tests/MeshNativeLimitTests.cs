using System.Reflection;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks compiler canonical output types used by Vulkan native memory accounting.</summary>
public sealed class MeshNativeLimitTests
{
    /// <summary>Counts emitted HLSL scalar/vector names without inventing extra position components.</summary>
    /// <param name="type">The compiler emitted canonical output type.</param>
    /// <param name="expected">The exact four-byte component count.</param>
    [Theory]
    [InlineData("float", 1UL)]
    [InlineData("float2", 2UL)]
    [InlineData("float3", 3UL)]
    [InlineData("float4", 4UL)]
    [InlineData("uint3", 3UL)]
    [InlineData("int4", 4UL)]
    public void NativeOutputComponentsMatchCompilerTypes(string type, ulong expected)
    {
        Type validation = typeof(VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanMeshValidation", true)!;
        MethodInfo components = validation.GetMethod("Components", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(expected, (ulong)components.Invoke(null, [type])!);
    }
}
