namespace Lumyte.Graphics.Vulkan;

/// <summary>Provides the cached shader address without boxing a Vulkan value handle.</summary>
internal interface IVulkanShaderBuffer
{
    /// <summary>Gets the cached address after validating native GPU access.</summary>
    ulong ShaderAddress { get; }
}
