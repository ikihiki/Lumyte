using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines lighting, transforms and texture references for model drawing.</summary>
/// <param name="Vertices">The object-space vertex range.</param>
/// <param name="World">The row-vector object-to-world transform.</param>
/// <param name="ViewProjection">The row-vector world-to-clip transform.</param>
/// <param name="NormalTransform">The inverse transpose of World.</param>
/// <param name="Material">The surface factors.</param>
/// <param name="CameraPosition">The world-space camera position.</param>
/// <param name="LightDirection">The world-space direction from the surface toward the light.</param>
/// <param name="LightColor">The linear directional-light radiance.</param>
/// <param name="AmbientColor">The linear ambient irradiance approximation.</param>
/// <param name="FogColor">The linear fog color.</param>
/// <param name="FogDensity">The nonnegative exponential fog density; zero disables fog.</param>
/// <param name="BaseColorTexture">The BaseColor texture or a neutral fallback.</param>
/// <param name="BaseColorSampler">The non-comparison BaseColor sampler.</param>
/// <param name="MetallicRoughnessTexture">The MetallicRoughness texture or a neutral fallback.</param>
/// <param name="MetallicRoughnessSampler">The non-comparison MetallicRoughness sampler.</param>
/// <param name="NormalTexture">The Normal texture or a neutral fallback.</param>
/// <param name="NormalSampler">The non-comparison Normal sampler.</param>
/// <param name="OcclusionTexture">The Occlusion texture or a neutral fallback.</param>
/// <param name="OcclusionSampler">The non-comparison Occlusion sampler.</param>
/// <param name="EmissiveTexture">The Emissive texture or a neutral fallback.</param>
/// <param name="EmissiveSampler">The non-comparison Emissive sampler.</param>
public readonly partial record struct ModelDrawArguments(IGpuRef<ModelVertex> Vertices, Matrix4x4 World, Matrix4x4 ViewProjection, Matrix4x4 NormalTransform, ModelMaterial Material, Vector3 CameraPosition, Vector3 LightDirection, Vector3 LightColor, Vector3 AmbientColor, Vector3 FogColor, float FogDensity, IGpuRef<IGraphicsTextureView> BaseColorTexture, IGpuRef<IGraphicsSampler> BaseColorSampler, IGpuRef<IGraphicsTextureView> MetallicRoughnessTexture, IGpuRef<IGraphicsSampler> MetallicRoughnessSampler, IGpuRef<IGraphicsTextureView> NormalTexture, IGpuRef<IGraphicsSampler> NormalSampler, IGpuRef<IGraphicsTextureView> OcclusionTexture, IGpuRef<IGraphicsSampler> OcclusionSampler, IGpuRef<IGraphicsTextureView> EmissiveTexture, IGpuRef<IGraphicsSampler> EmissiveSampler) : IShaderArguments
{
    // This uniform wire is deliberately distinct from ModelMaterial storage wire.
    // Fixed C# offsets preserve nested uniform padding without runtime name relocation.

    /// <inheritdoc/>
    static int IShaderArguments.WireSizeInBytes => 688;

    /// <inheritdoc/>
    void IShaderArguments.Write<TWriter>(ref TWriter writer)
    {
        writer.WriteReference(0, "Vertices", Vertices);
        writer.WriteValue(16, "World", World);
        writer.WriteValue(80, "ViewProjection", ViewProjection);
        writer.WriteValue(144, "NormalTransform", NormalTransform);
        writer.WriteValue(208, "Material.BaseColor", Material.BaseColor);
        writer.WriteValue(224, "Material.Emissive", Material.Emissive);
        writer.WriteValue(236, "Material.Metallic", Material.Metallic);
        writer.WriteValue(240, "Material.Roughness", Material.Roughness);
        writer.WriteValue(244, "Material.NormalScale", Material.NormalScale);
        writer.WriteValue(248, "Material.OcclusionStrength", Material.OcclusionStrength);
        writer.WriteValue(252, "Material.AlphaCutoff", Material.AlphaCutoff);
        writer.WriteValue(256, "Material.AlphaMode", Material.AlphaMode);
        writer.WriteValue(260, "Material.Unlit", Material.Unlit);
        writer.WriteValue(272, "Material.BaseColorUv.Offset", Material.BaseColorUv.Offset);
        writer.WriteValue(280, "Material.BaseColorUv.Scale", Material.BaseColorUv.Scale);
        writer.WriteValue(288, "Material.BaseColorUv.Rotation", Material.BaseColorUv.Rotation);
        writer.WriteValue(304, "Material.MetallicRoughnessUv.Offset", Material.MetallicRoughnessUv.Offset);
        writer.WriteValue(312, "Material.MetallicRoughnessUv.Scale", Material.MetallicRoughnessUv.Scale);
        writer.WriteValue(320, "Material.MetallicRoughnessUv.Rotation", Material.MetallicRoughnessUv.Rotation);
        writer.WriteValue(336, "Material.NormalUv.Offset", Material.NormalUv.Offset);
        writer.WriteValue(344, "Material.NormalUv.Scale", Material.NormalUv.Scale);
        writer.WriteValue(352, "Material.NormalUv.Rotation", Material.NormalUv.Rotation);
        writer.WriteValue(368, "Material.OcclusionUv.Offset", Material.OcclusionUv.Offset);
        writer.WriteValue(376, "Material.OcclusionUv.Scale", Material.OcclusionUv.Scale);
        writer.WriteValue(384, "Material.OcclusionUv.Rotation", Material.OcclusionUv.Rotation);
        writer.WriteValue(400, "Material.EmissiveUv.Offset", Material.EmissiveUv.Offset);
        writer.WriteValue(408, "Material.EmissiveUv.Scale", Material.EmissiveUv.Scale);
        writer.WriteValue(416, "Material.EmissiveUv.Rotation", Material.EmissiveUv.Rotation);
        writer.WriteValue(432, "Material.EmissiveStrength", new Vector4(Material.EmissiveStrength, 0, 0, 0));
        writer.WriteValue(448, "CameraPosition", CameraPosition);
        writer.WriteValue(464, "LightDirection", LightDirection);
        writer.WriteValue(480, "LightColor", LightColor);
        writer.WriteValue(496, "AmbientColor", AmbientColor);
        writer.WriteValue(512, "FogColor", FogColor);
        writer.WriteValue(524, "FogDensity", FogDensity);
        writer.WriteReference(528, "BaseColorTexture", BaseColorTexture);
        writer.WriteReference(544, "BaseColorSampler", BaseColorSampler);
        writer.WriteReference(560, "MetallicRoughnessTexture", MetallicRoughnessTexture);
        writer.WriteReference(576, "MetallicRoughnessSampler", MetallicRoughnessSampler);
        writer.WriteReference(592, "NormalTexture", NormalTexture);
        writer.WriteReference(608, "NormalSampler", NormalSampler);
        writer.WriteReference(624, "OcclusionTexture", OcclusionTexture);
        writer.WriteReference(640, "OcclusionSampler", OcclusionSampler);
        writer.WriteReference(656, "EmissiveTexture", EmissiveTexture);
        writer.WriteReference(672, "EmissiveSampler", EmissiveSampler);
    }
}
