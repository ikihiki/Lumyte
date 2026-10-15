using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed record GpuMesh(IGraphicsPipeline Pipeline, IArgumentTable Table, IGpuRef<GltfVertex> Vertices, uint VertexCount, Vector4 Color, IGpuRef<IGraphicsTextureView> Texture, IGpuRef<IGraphicsSampler> Sampler);
