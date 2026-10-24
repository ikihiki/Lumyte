using Lumyte.Memory;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed record CpuImage(ResourceId Id, int Width, int Height, IMemoryBuffer<byte> Pixels);
