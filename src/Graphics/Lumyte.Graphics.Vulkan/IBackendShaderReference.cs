using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

/// <summary>Certifies references manufactured inside this backend, not public lookalikes.</summary>
internal interface IBackendShaderReference : IShaderReference
{
}
