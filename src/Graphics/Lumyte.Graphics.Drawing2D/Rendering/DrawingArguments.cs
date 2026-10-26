using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal readonly partial record struct DrawingArguments(
    IGpuRef<DrawingInstruction> Instructions, IGpuRef<Vector4> Data,
    uint Count, uint Root, uint GraphMode, float Opacity,
    Vector4 SourceBounds, Vector4 OriginalBounds, Vector4 PixelOrigin,
    IGpuRef<IGraphicsTextureView> SourceTexture,
    IGpuRef<IGraphicsTextureView> OriginalTexture,
    IGpuRef<IGraphicsSampler> GraphSampler) : IShaderArguments;
