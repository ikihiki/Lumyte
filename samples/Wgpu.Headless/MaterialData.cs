using System.Numerics;

using Lumyte.Graphics;

namespace Lumyte.Samples;
/// <summary>
/// Stores logical base-color material values; serialization uses the reflected ABI, not C# struct layout.
/// </summary>
/// <param name="BaseColor">The finite base-color coefficient in linear RGBA order.</param>
/// <param name="BaseColorTexture">The optional logical texture registration; null uses only the coefficient.</param>
/// <param name="Sampler">The independently registered sampler.</param>
internal readonly record struct MaterialData(Vector4 BaseColor, TextureDescriptorReference? BaseColorTexture = null, SamplerDescriptorReference? Sampler = null);
