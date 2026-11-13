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
public readonly partial record struct ModelMaterial(Vector4 BaseColor, Vector3 Emissive, float Metallic, float Roughness, float NormalScale, float OcclusionStrength, float AlphaCutoff, uint AlphaMode, uint Unlit) : IShaderData
{
    /// <summary>Gets the base-color texture coordinate transform.</summary>
    public ModelTextureTransform BaseColorUv { get; init; } = ModelTextureTransform.Identity;

    /// <summary>Gets the metallic-roughness texture coordinate transform.</summary>
    public ModelTextureTransform MetallicRoughnessUv { get; init; } = ModelTextureTransform.Identity;

    /// <summary>Gets the normal texture coordinate transform.</summary>
    public ModelTextureTransform NormalUv { get; init; } = ModelTextureTransform.Identity;

    /// <summary>Gets the occlusion texture coordinate transform.</summary>
    public ModelTextureTransform OcclusionUv { get; init; } = ModelTextureTransform.Identity;

    /// <summary>Gets the emissive texture coordinate transform.</summary>
    public ModelTextureTransform EmissiveUv { get; init; } = ModelTextureTransform.Identity;

    /// <summary>Gets the linear emissive multiplier, including values greater than one for HDR.</summary>
    public float EmissiveStrength { get; init; } = 1;

    /// <inheritdoc/>
    static int IShaderArguments.WireSizeInBytes => 172;

    /// <inheritdoc/>
    void IShaderArguments.Write<TWriter>(ref TWriter writer)
    {
        writer.WriteValue(0, "BaseColor", BaseColor);
        writer.WriteValue(16, "Emissive", Emissive);
        writer.WriteValue(28, "Metallic", Metallic);
        writer.WriteValue(32, "Roughness", Roughness);
        writer.WriteValue(36, "NormalScale", NormalScale);
        writer.WriteValue(40, "OcclusionStrength", OcclusionStrength);
        writer.WriteValue(44, "AlphaCutoff", AlphaCutoff);
        writer.WriteValue(48, "AlphaMode", AlphaMode);
        writer.WriteValue(52, "Unlit", Unlit);
        writer.WriteValue(56, "BaseColorUv.Offset", BaseColorUv.Offset);
        writer.WriteValue(64, "BaseColorUv.Scale", BaseColorUv.Scale);
        writer.WriteValue(72, "BaseColorUv.Rotation", BaseColorUv.Rotation);
        writer.WriteValue(76, "MetallicRoughnessUv.Offset", MetallicRoughnessUv.Offset);
        writer.WriteValue(84, "MetallicRoughnessUv.Scale", MetallicRoughnessUv.Scale);
        writer.WriteValue(92, "MetallicRoughnessUv.Rotation", MetallicRoughnessUv.Rotation);
        writer.WriteValue(96, "NormalUv.Offset", NormalUv.Offset);
        writer.WriteValue(104, "NormalUv.Scale", NormalUv.Scale);
        writer.WriteValue(112, "NormalUv.Rotation", NormalUv.Rotation);
        writer.WriteValue(116, "OcclusionUv.Offset", OcclusionUv.Offset);
        writer.WriteValue(124, "OcclusionUv.Scale", OcclusionUv.Scale);
        writer.WriteValue(132, "OcclusionUv.Rotation", OcclusionUv.Rotation);
        writer.WriteValue(136, "EmissiveUv.Offset", EmissiveUv.Offset);
        writer.WriteValue(144, "EmissiveUv.Scale", EmissiveUv.Scale);
        writer.WriteValue(152, "EmissiveUv.Rotation", EmissiveUv.Rotation);
        writer.WriteValue(156, nameof(EmissiveStrength), new Vector4(EmissiveStrength, 0, 0, 0));
    }
}
