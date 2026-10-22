using Lumyte.Memory;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed record CpuMesh(ResourceAddress Address, IMemoryBuffer<GltfVertex> Vertices, CpuMaterial Material);
