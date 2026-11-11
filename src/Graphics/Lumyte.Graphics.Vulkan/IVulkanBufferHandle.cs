namespace Lumyte.Graphics.Vulkan;

/// <summary>Provides a typed non-owning buffer handle inside the backend without boxing.</summary>
internal interface IVulkanBufferHandle
{
    /// <summary>Gets the existing native allocation handle without boxing.</summary>
    Silk.NET.Vulkan.Buffer NativeBuffer { get; }
}
