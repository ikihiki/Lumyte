using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines lighting, transforms and texture references for model drawing.</summary>
public partial record struct ModelDrawArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="ModelDrawArguments"/> structure.</summary>
    /// <param name="vertices">The object-space vertex range.</param>
    /// <param name="world">The row-vector object-to-world transform.</param>
    /// <param name="viewProjection">The row-vector world-to-clip transform.</param>
    /// <param name="normalTransform">The inverse transpose of World.</param>
    /// <param name="material">The surface factors.</param>
    /// <param name="cameraPosition">The world-space camera position.</param>
    /// <param name="lightDirection">The world-space direction from the surface toward the light.</param>
    /// <param name="lightColor">The linear directional-light radiance.</param>
    /// <param name="ambientColor">The linear ambient irradiance approximation.</param>
    /// <param name="fogColor">The linear fog color.</param>
    /// <param name="fogDensity">The nonnegative exponential fog density; zero disables fog.</param>
    /// <param name="baseColorTexture">The BaseColor texture or a neutral fallback.</param>
    /// <param name="baseColorSampler">The non-comparison BaseColor sampler.</param>
    /// <param name="metallicRoughnessTexture">The MetallicRoughness texture or a neutral fallback.</param>
    /// <param name="metallicRoughnessSampler">The non-comparison MetallicRoughness sampler.</param>
    /// <param name="normalTexture">The Normal texture or a neutral fallback.</param>
    /// <param name="normalSampler">The non-comparison Normal sampler.</param>
    /// <param name="occlusionTexture">The Occlusion texture or a neutral fallback.</param>
    /// <param name="occlusionSampler">The non-comparison Occlusion sampler.</param>
    /// <param name="emissiveTexture">The Emissive texture or a neutral fallback.</param>
    /// <param name="emissiveSampler">The non-comparison Emissive sampler.</param>
    public ModelDrawArguments(IGpuRef<ModelVertex> vertices, Matrix4x4 world, Matrix4x4 viewProjection, Matrix4x4 normalTransform, ModelMaterial material, Vector3 cameraPosition, Vector3 lightDirection, Vector3 lightColor, Vector3 ambientColor, Vector3 fogColor, float fogDensity, IGpuRef<IGraphicsTextureView> baseColorTexture, IGpuRef<IGraphicsSampler> baseColorSampler, IGpuRef<IGraphicsTextureView> metallicRoughnessTexture, IGpuRef<IGraphicsSampler> metallicRoughnessSampler, IGpuRef<IGraphicsTextureView> normalTexture, IGpuRef<IGraphicsSampler> normalSampler, IGpuRef<IGraphicsTextureView> occlusionTexture, IGpuRef<IGraphicsSampler> occlusionSampler, IGpuRef<IGraphicsTextureView> emissiveTexture, IGpuRef<IGraphicsSampler> emissiveSampler)
    {
        _data = new()
        {
            World = world,
            ViewProjection = viewProjection,
            NormalTransform = normalTransform,
            Material = material,
            CameraPosition = cameraPosition,
            LightDirection = lightDirection,
            LightColor = lightColor,
            AmbientColor = ambientColor,
            FogColor = fogColor,
            FogDensity = fogDensity,
        };

        Vertices = vertices;
        BaseColorTexture = baseColorTexture;
        BaseColorSampler = baseColorSampler;
        MetallicRoughnessTexture = metallicRoughnessTexture;
        MetallicRoughnessSampler = metallicRoughnessSampler;
        NormalTexture = normalTexture;
        NormalSampler = normalSampler;
        OcclusionTexture = occlusionTexture;
        OcclusionSampler = occlusionSampler;
        EmissiveTexture = emissiveTexture;
        EmissiveSampler = emissiveSampler;
    }

    /// <summary>Gets or sets the shader Vertices value.</summary>
    public IGpuRef<ModelVertex> Vertices { get; set; }

    /// <summary>Gets or sets the shader World value.</summary>
    public partial Matrix4x4 World { get; set; }

    /// <summary>Gets or sets the shader ViewProjection value.</summary>
    public partial Matrix4x4 ViewProjection { get; set; }

    /// <summary>Gets or sets the shader NormalTransform value.</summary>
    public partial Matrix4x4 NormalTransform { get; set; }

    /// <summary>Gets or sets the shader Material value.</summary>
    public partial ModelMaterial Material { get; set; }

    /// <summary>Gets or sets the shader CameraPosition value.</summary>
    public partial Vector3 CameraPosition { get; set; }

    /// <summary>Gets or sets the shader LightDirection value.</summary>
    public partial Vector3 LightDirection { get; set; }

    /// <summary>Gets or sets the shader LightColor value.</summary>
    public partial Vector3 LightColor { get; set; }

    /// <summary>Gets or sets the shader AmbientColor value.</summary>
    public partial Vector3 AmbientColor { get; set; }

    /// <summary>Gets or sets the shader FogColor value.</summary>
    public partial Vector3 FogColor { get; set; }

    /// <summary>Gets or sets the shader FogDensity value.</summary>
    public partial float FogDensity { get; set; }

    /// <summary>Gets or sets the shader BaseColorTexture value.</summary>
    public IGpuRef<IGraphicsTextureView> BaseColorTexture { get; set; }

    /// <summary>Gets or sets the shader BaseColorSampler value.</summary>
    public IGpuRef<IGraphicsSampler> BaseColorSampler { get; set; }

    /// <summary>Gets or sets the shader MetallicRoughnessTexture value.</summary>
    public IGpuRef<IGraphicsTextureView> MetallicRoughnessTexture { get; set; }

    /// <summary>Gets or sets the shader MetallicRoughnessSampler value.</summary>
    public IGpuRef<IGraphicsSampler> MetallicRoughnessSampler { get; set; }

    /// <summary>Gets or sets the shader NormalTexture value.</summary>
    public IGpuRef<IGraphicsTextureView> NormalTexture { get; set; }

    /// <summary>Gets or sets the shader NormalSampler value.</summary>
    public IGpuRef<IGraphicsSampler> NormalSampler { get; set; }

    /// <summary>Gets or sets the shader OcclusionTexture value.</summary>
    public IGpuRef<IGraphicsTextureView> OcclusionTexture { get; set; }

    /// <summary>Gets or sets the shader OcclusionSampler value.</summary>
    public IGpuRef<IGraphicsSampler> OcclusionSampler { get; set; }

    /// <summary>Gets or sets the shader EmissiveTexture value.</summary>
    public IGpuRef<IGraphicsTextureView> EmissiveTexture { get; set; }

    /// <summary>Gets or sets the shader EmissiveSampler value.</summary>
    public IGpuRef<IGraphicsSampler> EmissiveSampler { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Vertices;
        public Matrix4x4 World;
        public Matrix4x4 ViewProjection;
        public Matrix4x4 NormalTransform;
        public ModelMaterial Material;
        public Vector3 CameraPosition;
        public Vector3 LightDirection;
        public Vector3 LightColor;
        public Vector3 AmbientColor;
        public Vector3 FogColor;
        public float FogDensity;
        public ShaderReferenceSlot BaseColorTexture;
        public ShaderReferenceSlot BaseColorSampler;
        public ShaderReferenceSlot MetallicRoughnessTexture;
        public ShaderReferenceSlot MetallicRoughnessSampler;
        public ShaderReferenceSlot NormalTexture;
        public ShaderReferenceSlot NormalSampler;
        public ShaderReferenceSlot OcclusionTexture;
        public ShaderReferenceSlot OcclusionSampler;
        public ShaderReferenceSlot EmissiveTexture;
        public ShaderReferenceSlot EmissiveSampler;
    }
}
