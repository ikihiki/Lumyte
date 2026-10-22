using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed record GpuTexture(IGraphicsTexture Texture, IGraphicsTextureView View);
