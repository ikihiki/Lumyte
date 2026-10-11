using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines metallic-roughness surface factors in linear color space.</summary>
/// <param name="BaseColor">The straight-alpha base color factor.</param>
/// <param name="Emissive">The emissive factor.</param>
/// <param name="Metallic">The metallic factor in [0, 1].</param>
/// <param name="Roughness">The roughness factor in [0, 1].</param>
/// <param name="NormalScale">The tangent-space normal XY scale.</param>
/// <param name="OcclusionStrength">The occlusion strength in [0, 1].</param>
/// <param name="AlphaCutoff">The mask threshold.</param>
/// <param name="AlphaMode">Zero for opaque, one for mask, two for blend.</param>
/// <param name="Unlit">One to bypass lighting, zero for metallic-roughness shading.</param>
public readonly partial record struct ModelMaterial(Vector4 BaseColor, Vector3 Emissive, float Metallic, float Roughness, float NormalScale, float OcclusionStrength, float AlphaCutoff, uint AlphaMode, uint Unlit) : IShaderData;
