using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

/// <summary>Certifies references manufactured inside this backend, not public lookalikes.</summary>
internal interface IBackendShaderReference : IShaderReference
{
}
