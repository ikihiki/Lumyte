using Lumyte.Memory;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed record CpuMesh(IMemoryBuffer<GltfVertex> Vertices, CpuMaterial Material);
