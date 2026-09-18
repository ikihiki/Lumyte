using System.Numerics;
using System.Collections.Immutable;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public enum ModelAlphaMode { Opaque, Mask, Blend }
public enum ModelTextureFilter { Nearest, Linear }
public enum ModelMipFilter { None, Nearest, Linear }
public enum ModelTextureWrap { ClampToEdge, MirroredRepeat, Repeat }
public sealed record ModelSamplerData(ModelTextureFilter MinFilter = ModelTextureFilter.Linear,
    ModelTextureFilter MagFilter = ModelTextureFilter.Linear, ModelMipFilter MipFilter = ModelMipFilter.Linear,
    ModelTextureWrap WrapU = ModelTextureWrap.Repeat, ModelTextureWrap WrapV = ModelTextureWrap.Repeat);
public sealed record ModelTextureData(GpuImageUploadData Image, ModelSamplerData Sampler);
public sealed record ModelTextureUse(ModelTextureData Texture, int TexCoordSet = 0)
{
    public Matrix3x2 Transform { get; init; } = Matrix3x2.Identity;
}
public sealed record ModelMaterialData(GpuUploadDataKey Key) : IGpuUploadData
{
    public Vector4 BaseColorFactor { get; init; } = Vector4.One;
    public float MetallicFactor { get; init; } = 1;
    public float RoughnessFactor { get; init; } = 1;
    public Vector3 EmissiveFactor { get; init; }
    public float EmissiveStrength { get; init; } = 1;
    public ModelAlphaMode AlphaMode { get; init; }
    public float AlphaCutoff { get; init; } = .5f;
    public bool DoubleSided { get; init; }
    public bool Unlit { get; init; }
    public ModelTextureUse? BaseColorTexture { get; init; }
    public ModelTextureUse? MetallicRoughnessTexture { get; init; }
    public ModelTextureUse? NormalTexture { get; init; }
    public ModelTextureUse? OcclusionTexture { get; init; }
    public ModelTextureUse? EmissiveTexture { get; init; }
    public float NormalScale { get; init; } = 1;
    public float OcclusionStrength { get; init; } = 1;
    public IEnumerable<ModelTextureUse?> Textures => [BaseColorTexture, MetallicRoughnessTexture, NormalTexture, OcclusionTexture, EmissiveTexture];
}

public sealed record ModelCamera
{
    private ModelCamera(Vector3 eye, Vector3 target, Vector3 up, float size, float near, float far, bool orthographic)
    { Eye = eye; View = Matrix4x4.CreateLookAt(eye, target, up); Size = size; Near = near; Far = far; IsOrthographic = orthographic; }
    public Vector3 Eye { get; }
    public Matrix4x4 View { get; }
    public float Size { get; }
    public float Near { get; }
    public float Far { get; }
    public bool IsOrthographic { get; }
    public float? FixedAspect { get; init; }
    public static ModelCamera Perspective(Vector3 eye, Vector3 target, Vector3 up, float verticalFoV, float near, float far = float.PositiveInfinity)
        => new(eye, target, up, verticalFoV, near, far, false);
    public static ModelCamera Orthographic(Vector3 eye, Vector3 target, Vector3 up, float verticalSize, float near, float far)
        => new(eye, target, up, verticalSize, near, far, true);
    public Matrix4x4 GetViewProjection(float outputAspect)
    {
        float aspect = FixedAspect ?? outputAspect;
        Matrix4x4 projection = IsOrthographic ? Matrix4x4.CreateOrthographic(Size * aspect, Size, Near, Far)
            : Matrix4x4.CreatePerspectiveFieldOfView(Size, aspect, Near, Far);
        // Fit the fixed camera aspect inside the target while preserving the full clear area.
        if (aspect > outputAspect) { projection *= Matrix4x4.CreateScale(1, outputAspect / aspect, 1); }
        else { projection *= Matrix4x4.CreateScale(aspect / outputAspect, 1, 1); }
        return View * projection;
    }
}
public enum ModelLightKind { Directional, Point, Spot }
public sealed record ModelLight(ModelLightKind Kind, Vector3 Position, Vector3 Direction, Vector3 Color, float Intensity,
    float? Range = null, float InnerAngle = 0, float OuterAngle = MathF.PI / 4)
{
    public static ModelLight Directional(Vector3 direction, Vector3 color, float intensity) => new(ModelLightKind.Directional, default, Vector3.Normalize(direction), color, intensity);
    public static ModelLight Point(Vector3 position, Vector3 color, float intensity, float? range = null) => new(ModelLightKind.Point, position, default, color, intensity, range);
    public static ModelLight Spot(Vector3 position, Vector3 direction, Vector3 color, float intensity, float? range = null, float innerAngle = 0, float outerAngle = MathF.PI / 4)
        => new(ModelLightKind.Spot, position, Vector3.Normalize(direction), color, intensity, range, innerAngle, outerAngle);
}
/// <summary>Decoded equirectangular environment radiance; rotation maps environment directions into world space.</summary>
public sealed record ModelEnvironment(GpuImageUploadData Image, Quaternion Rotation, float Intensity = 1)
{
    public ModelEnvironment(GpuImageUploadData image) : this(image, Quaternion.Identity) { }
}
public sealed class ModelLighting(IEnumerable<ModelLight> lights, ModelEnvironment? environment = null)
{
    public static ModelLighting Empty { get; } = new([]);
    public ImmutableArray<ModelLight> Lights { get; } = lights.ToImmutableArray();
    public ModelEnvironment? Environment { get; } = environment;
}
