using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Separately reports unavailable native stencil format coverage.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MeshStencilGpuFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="MeshStencilGpuFactAttribute"/> class.</summary>
    public MeshStencilGpuFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 with a working native driver.";
            return;
        }

        using var device = VulkanDevice.Create();
        if ((device.Caps.Features & GraphicsFeatures.MeshShader) == 0)
        {
            Skip = "The selected Vulkan device does not enable Mesh shaders.";
            return;
        }

        try
        {
            using IGraphicsTexture texture = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Depth24Stencil8, Usage = TextureUsage.RenderAttachment });
        }
        catch (NotSupportedException)
        {
            Skip = "The selected Vulkan device does not support Depth24Stencil8 render attachments; native stencil coverage is unavailable.";
        }
    }
}
