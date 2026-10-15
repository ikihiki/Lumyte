using Lumyte.Memory;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed record CpuImage(int Width, int Height, IMemoryBuffer<byte> Pixels);
