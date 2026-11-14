namespace Lumyte.Graphics.Vulkan;

/// <summary>Provides the native buffer handle without boxing a Vulkan value handle.</summary>
internal interface IVulkanShaderBuffer
{
    /// <summary>Gets the native buffer used by address-based shader references.</summary>
    Silk.NET.Vulkan.Buffer ShaderBuffer { get; }
}
