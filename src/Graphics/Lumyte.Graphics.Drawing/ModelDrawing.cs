using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing.Gltf;

namespace Lumyte.Graphics.Drawing;

/// <summary>Provides explicit transform and render-state setup for model drawing.</summary>
public static class ModelDrawing
{
    /// <summary>Gets the glTF default metallic-roughness factors.</summary>
    public static ModelMaterial DefaultMaterial => new(Vector4.One, Vector3.Zero, 1, 1, 1, 1, 0.5f, 0, 0);

    /// <summary>Builds model root values with identity transforms, default lighting and explicit neutral texture fallbacks.</summary>
    /// <param name="vertices">The registered model vertex range.</param>
    /// <param name="material">The loaded glTF material.</param>
    /// <param name="white">The white texture and sampler used for missing color, scalar and emissive slots.</param>
    /// <param name="neutralNormal">The linear (0.5, 0.5, 1) texture and sampler used for missing normals.</param>
    /// <param name="resolveTexture">The resolver for glTF texture indices, with the correct color space and uploaded mip chain.</param>
    /// <returns>Root values to customize with camera, transforms, light and fog before drawing.</returns>
    public static ModelDrawArguments CreateArguments(
        IGpuRef<ModelVertex> vertices,
        GltfMaterial material,
        SampledTexture white,
        SampledTexture neutralNormal,
        Func<int, ModelTextureRole, SampledTexture>? resolveTexture = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(material);
        SampledTexture baseColor = Resolve(material.BaseColorTexture, ModelTextureRole.BaseColor, white);
        SampledTexture metallicRoughness = Resolve(material.MetallicRoughnessTexture, ModelTextureRole.MetallicRoughness, white);
        SampledTexture normal = Resolve(material.NormalTexture, ModelTextureRole.Normal, neutralNormal);
        SampledTexture occlusion = Resolve(material.OcclusionTexture, ModelTextureRole.Occlusion, white);
        SampledTexture emissive = Resolve(material.EmissiveTexture, ModelTextureRole.Emissive, white);
        return new(vertices, Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity, material.Factors, new(0, 0, 1), Vector3.UnitZ, Vector3.One, new(0.03f), Vector3.Zero, 0, baseColor.Texture, baseColor.Sampler, metallicRoughness.Texture, metallicRoughness.Sampler, normal.Texture, normal.Sampler, occlusion.Texture, occlusion.Sampler, emissive.Texture, emissive.Sampler);

        SampledTexture Resolve(int? index, ModelTextureRole role, SampledTexture fallback)
        {
            SampledTexture result = index.HasValue
                ? (resolveTexture ?? throw new ArgumentException("A textured material requires a texture resolver.", nameof(resolveTexture)))(index.Value, role)
                : fallback;
            ArgumentNullException.ThrowIfNull(result.Texture);
            ArgumentNullException.ThrowIfNull(result.Sampler);
            return result;
        }
    }

    /// <summary>Computes the row-vector normal transform for nonuniform scale.</summary>
    /// <param name="world">The invertible object-to-world matrix.</param>
    /// <returns>The inverse transpose matrix.</returns>
    public static Matrix4x4 CreateNormalTransform(Matrix4x4 world)
    {
        if (!Matrix4x4.Invert(world, out Matrix4x4 inverse))
        {
            throw new ArgumentException("The model world transform must be invertible.", nameof(world));
        }

        return Matrix4x4.Transpose(inverse);
    }

    /// <summary>Creates depth, straight-alpha blend and face-culling state for one color/depth pass.</summary>
    /// <param name="material">The material surface flags.</param>
    /// <param name="mirrored">Whether the world transform reverses orientation.</param>
    /// <returns>The explicit state; blend instances must still be sorted back to front by the caller.</returns>
    public static GraphicsRenderStateDesc CreateRenderState(GltfMaterial material, bool mirrored = false)
    {
        ArgumentNullException.ThrowIfNull(material);
        bool blend = material.Factors.AlphaMode == 2;
        return new()
        {
            DepthStencil = new() { DepthTestEnable = true, DepthWriteEnable = !blend },
            Rasterization = new()
            {
                Cull = material.DoubleSided ? CullMode.None : CullMode.Back,
                FrontFace = mirrored ? FrontFace.Clockwise : FrontFace.CounterClockwise,
            },
            ColorTargets =
            [
                new()
                {
                    BlendEnable = blend,
                    Color = new() { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha },
                    Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha },
                },
            ],
        };
    }
}
