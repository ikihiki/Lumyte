using System.Numerics;
using Lumyte.Graphics;

namespace Lumyte.Samples;

// Scene-owned buffer contents: an NDC rectangle and its opaque sampled texture.
internal readonly record struct SquareMaterial(Vector4 Rectangle, SampledTexture2DReference Texture);
