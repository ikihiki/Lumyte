using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.ImageEffects;

internal readonly partial record struct EffectArguments(
    IGpuRef<IGraphicsTextureView> Input,
    IGpuRef<IGraphicsTextureView> Backdrop,
    IGpuRef<IGraphicsSampler> Sampler,
    Vector4 InputBounds,
    Vector4 BackdropBounds,
    Vector4 OutputBounds,
    Vector4 Settings,
    Vector4 Row0,
    Vector4 Row1,
    Vector4 Row2,
    Vector4 Row3,
    Vector4 Constant) : IShaderArguments;
