using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing.Gltf;

/// <summary>Associates an image with glTF sampling state.</summary>
/// <param name="ImageIndex">The model image index.</param>
/// <param name="Sampler">The glTF sampler settings; LOD must match uploaded mip levels.</param>
public sealed record GltfTexture(int ImageIndex, SamplerDesc Sampler);
