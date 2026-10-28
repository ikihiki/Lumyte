using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks managed rasterization contracts without creating a GPU device.</summary>
public sealed class PipelineContractTests
{
    /// <summary>Rejects unsupported clipping changes instead of silently retaining native defaults.</summary>
    /// <param name="backend">The backend validation implementation.</param>
    [Theory]
    [InlineData("Wgpu")]
    [InlineData("Vulkan")]
    public void DisablingDepthClippingIsRejected(string backend)
    {
        var state = new GraphicsRenderStateDesc { Rasterization = new() { DepthClipEnable = false } };
        TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => Validate(backend, state, new()));
        Assert.IsType<NotSupportedException>(error.InnerException);
    }

    /// <summary>Requires the advertised feature for both positive and negative bias clamps.</summary>
    /// <param name="backend">The backend validation implementation.</param>
    /// <param name="clamp">The requested nonzero clamp.</param>
    [Theory]
    [InlineData("Wgpu", 0.5f)]
    [InlineData("Wgpu", -0.5f)]
    [InlineData("Vulkan", 0.5f)]
    [InlineData("Vulkan", -0.5f)]
    public void BiasClampRequiresEnabledFeature(string backend, float clamp)
    {
        var state = new GraphicsRenderStateDesc { Rasterization = new() { DepthBiasConstant = 4, DepthBiasClamp = clamp } };
        TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => Validate(backend, state, new()));
        Assert.IsType<NotSupportedException>(error.InnerException);
    }

    /// <summary>Preserves the supported clamp in the immutable native state input.</summary>
    [Fact]
    public void EnabledVulkanBiasClampIsPreserved()
    {
        var state = new GraphicsRenderStateDesc { Rasterization = new() { DepthBiasConstant = 4, DepthBiasClamp = 0.5f } };
        object snapshot = Validate("Vulkan", state, new() { Features = GraphicsFeatures.DepthBiasClamp });
        GraphicsRenderStateDesc captured = Assert.IsType<GraphicsRenderStateDesc>(snapshot.GetType().GetProperty("Desc")!.GetValue(snapshot));
        Assert.Equal(0.5f, captured.Rasterization.DepthBiasClamp);
    }

    /// <summary>Retains the portable zero-clamp default without requiring the optional feature.</summary>
    /// <param name="backend">The backend validation implementation.</param>
    [Theory]
    [InlineData("Wgpu")]
    [InlineData("Vulkan")]
    public void DefaultRasterizationRemainsPortable(string backend) => Assert.NotNull(Validate(backend, new(), new()));

    private static object Validate(string backend, GraphicsRenderStateDesc state, DeviceCaps caps)
    {
        Assembly assembly = backend == "Vulkan" ? typeof(VulkanDevice).Assembly : typeof(WgpuDevice).Assembly;
        Type validation = assembly.GetType("Lumyte.Graphics." + backend + ".PipelineValidation", throwOnError: true)!;
        return validation.GetMethod("State", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [state, caps])!;
    }
}
