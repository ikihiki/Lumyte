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
public readonly partial record struct ModelDrawArguments(IGpuRef<ModelVertex> Vertices, Matrix4x4 World, Matrix4x4 ViewProjection, Matrix4x4 NormalTransform, ModelMaterial Material, Vector3 CameraPosition, Vector3 LightDirection, Vector3 LightColor, Vector3 AmbientColor, Vector3 FogColor, float FogDensity, IGpuRef<IGraphicsTextureView> BaseColorTexture, IGpuRef<IGraphicsSampler> BaseColorSampler, IGpuRef<IGraphicsTextureView> MetallicRoughnessTexture, IGpuRef<IGraphicsSampler> MetallicRoughnessSampler, IGpuRef<IGraphicsTextureView> NormalTexture, IGpuRef<IGraphicsSampler> NormalSampler, IGpuRef<IGraphicsTextureView> OcclusionTexture, IGpuRef<IGraphicsSampler> OcclusionSampler, IGpuRef<IGraphicsTextureView> EmissiveTexture, IGpuRef<IGraphicsSampler> EmissiveSampler) : IShaderArguments;
