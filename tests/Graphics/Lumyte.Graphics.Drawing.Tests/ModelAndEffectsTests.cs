using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Checks compiled shader ABI compatibility and model rendering state without a GPU.</summary>
public sealed class ModelAndEffectsTests
{
    /// <summary>Checks model and effect codecs pack against real reflection for both targets.</summary>
    [Fact]
    public void ModelAndEffectSchemasPackGeneratedValues()
    {
        var vertex = new ModelVertex(Vector3.UnitX, Vector3.UnitY, new(1, 0, 0, -1), new(0.25f, 0.5f), Vector4.One);
        ModelMaterial material = ModelDrawing.DefaultMaterial;
        ModelDrawArguments arguments = default(ModelDrawArguments) with
        {
            Material = material,
            World = Matrix4x4.Identity,
            ViewProjection = Matrix4x4.CreateTranslation(1, 2, 3),
            NormalTransform = Matrix4x4.Identity,
            LightDirection = Vector3.UnitZ,
            LightColor = Vector3.One,
        };
        PostProcessArguments effects = default(PostProcessArguments) with { Exposure = 1, Saturation = 0.5f, AspectRatio = 2, Vignette = 0.25f, ToneMap = 1 };
        foreach (ShaderTarget target in Enum.GetValues<ShaderTarget>())
        {
            ShaderTargetData vs = DrawingShaders.LoadModelVertex().GetTarget(target);
            ShaderTargetData fs = DrawingShaders.LoadModelFragment().GetTarget(target);
            ShaderDataLayout.ValidateProgram(vs, fs);
            var layout = ShaderDataLayout.Data(vs, "ModelVertex");
            byte[] packed = layout.Pack(IShaderArguments.Capture(in vertex), (_, _) => throw new InvalidOperationException());
            Assert.Equal(layout.Size, packed.Length);
            Assert.True(layout.Matches(ShaderDataLayout.Data(fs, "ModelVertex")));
            Assert.Equal(1, BitConverter.ToSingle(packed, 0));
            var materialLayout = ShaderDataLayout.Data(vs, "ModelMaterial");
            Assert.NotEmpty(materialLayout.Pack(IShaderArguments.Capture(in material), (_, _) => throw new InvalidOperationException()));
            int references = 0;
            var root = ShaderDataLayout.Root(vs, "arguments");
            Assert.NotEmpty(root.Pack(IShaderArguments.Capture(in arguments), (_, _) =>
            {
                references++;
                return new byte[16];
            }));
            Assert.Equal(11, references);
            ShaderTargetData effectVertex = DrawingShaders.LoadPostProcessVertex().GetTarget(target);
            ShaderTargetData effectFragment = DrawingShaders.LoadPostProcessFragment().GetTarget(target);
            Assert.Same(effectFragment, ShaderDataLayout.RootTarget(effectVertex, effectFragment, "arguments"));
            Assert.NotEmpty(ShaderDataLayout.Root(effectFragment, "arguments").Pack(IShaderArguments.Capture(in effects), (_, _) => new byte[16]));
        }
    }

    /// <summary>Checks inverse-transpose normals remain perpendicular under nonuniform scale.</summary>
    [Fact]
    public void NormalTransformHandlesNonuniformScale()
    {
        Matrix4x4 world = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateRotationY(0.6f);
        Matrix4x4 transform = ModelDrawing.CreateNormalTransform(world);
        var normal = Vector3.Normalize(new(1, 1, 0));
        var tangent = Vector3.Normalize(new(1, -1, 0));
        var transformedNormal = Vector3.TransformNormal(normal, transform);
        var transformedTangent = Vector3.TransformNormal(tangent, world);
        Assert.InRange(MathF.Abs(Vector3.Dot(transformedNormal, transformedTangent)), 0, 1e-5f);
        Assert.Throws<ArgumentException>(() => ModelDrawing.CreateNormalTransform(Matrix4x4.CreateScale(0)));
    }

    /// <summary>Checks blend, depth writes, double-sided and mirrored winding are selected together.</summary>
    [Fact]
    public void MaterialRenderStateHandlesAlphaAndOrientation()
    {
        ModelMaterial material = ModelDrawing.DefaultMaterial;
        GraphicsRenderStateDesc opaque = ModelDrawing.CreateRenderState(material);
        Assert.True(opaque.DepthStencil.DepthWriteEnable);
        Assert.False(opaque.ColorTargets[0].BlendEnable);
        Assert.Equal(CullMode.Back, opaque.Rasterization.Cull);
        GraphicsRenderStateDesc blend = ModelDrawing.CreateRenderState(material with { AlphaMode = 2 }, doubleSided: true, mirrored: true);
        Assert.True(blend.DepthStencil.DepthTestEnable);
        Assert.False(blend.DepthStencil.DepthWriteEnable);
        Assert.Equal(BlendFactor.SourceAlpha, blend.ColorTargets[0].Color.Source);
        Assert.Equal(CullMode.None, blend.Rasterization.Cull);
        Assert.Equal(FrontFace.Clockwise, blend.Rasterization.FrontFace);
    }

    /// <summary>Checks default model arguments use explicitly provided neutral texture references.</summary>
    [Fact]
    public void DefaultArgumentsUseNeutralTextureReferences()
    {
        var white = new SampledTexture(new TestReference<IGraphicsTextureView>(), new TestReference<IGraphicsSampler>());
        var normal = new SampledTexture(new TestReference<IGraphicsTextureView>(), new TestReference<IGraphicsSampler>());
        ModelMaterial material = ModelDrawing.DefaultMaterial;
        ModelDrawArguments arguments = ModelDrawing.CreateArguments(new TestReference<ModelVertex>(), material, white, normal);
        Assert.Same(white.Texture, arguments.BaseColorTexture);
        Assert.Same(white.Texture, arguments.MetallicRoughnessTexture);
        Assert.Same(normal.Texture, arguments.NormalTexture);
        Assert.Same(normal.Sampler, arguments.NormalSampler);
        Assert.Equal(material, arguments.Material);
        Assert.Throws<ArgumentNullException>(() => ModelDrawing.CreateArguments(new TestReference<ModelVertex>(), material, default, normal));
    }

    private sealed class TestReference<T> : IGpuRef<T>
    {
        public ulong Count => 1;

        public IGpuRef<T> GetElement(ulong index) => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));
    }
}
