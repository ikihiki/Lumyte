using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks offline and online shader modules through the common API.</summary>
public sealed class ShaderTests
{
    /// <summary>Checks embedded and freshly compiled WGSL modules on wgpu.</summary>
    /// <returns>The completion of the shader checks.</returns>
    [GpuFact]
    public async Task WgpuLoadsAndCompilesShadersAsync()
    {
        using var device = WgpuDevice.Create();
        Assert.Contains("Shader checks passed", ShaderExercise.Run(device));
        Assert.Contains("Online shader checks passed", await ShaderExercise.RunOnlineAsync(device, new SlangShaderCompiler()));
    }

    /// <summary>Checks embedded and freshly compiled SPIR-V modules on Vulkan.</summary>
    /// <returns>The completion of the shader checks.</returns>
    [GpuFact]
    public async Task VulkanLoadsAndCompilesShadersAsync()
    {
        using var device = VulkanDevice.Create();
        Assert.Contains("Shader checks passed", ShaderExercise.Run(device));
        Assert.Contains("Online shader checks passed", await ShaderExercise.RunOnlineAsync(device, new SlangShaderCompiler()));
    }
}
