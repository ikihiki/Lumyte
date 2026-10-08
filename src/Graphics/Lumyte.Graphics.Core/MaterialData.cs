using System.Numerics;

namespace Lumyte.Graphics;
/// <summary>
/// Stores logical base-color material values; serialization uses the reflected ABI, not C# struct layout.
/// </summary>
/// <param name="BaseColor">The finite base-color coefficient in linear RGBA order.</param>
/// <param name="BaseColorTexture">The optional non-owning sampled pair; null uses only the coefficient.</param>
public readonly record struct MaterialData(Vector4 BaseColor, SampledTexture2DReference? BaseColorTexture = null);
