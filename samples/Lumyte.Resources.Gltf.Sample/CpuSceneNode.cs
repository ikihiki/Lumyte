using System.Numerics;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed record CpuSceneNode(int Index, Matrix4x4 Local, Matrix4x4 World, CpuMesh? Mesh, IReadOnlyList<CpuSceneNode> Children);
