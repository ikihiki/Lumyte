using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal readonly partial record struct DrawingInstruction(
    Vector4 T0, Vector4 T1, Vector4 A, Vector4 B, Vector4 C, Vector4 D,
    uint Kind, uint First, uint Count, uint Mode,
    IGpuRef<IGraphicsTextureView> Texture, IGpuRef<IGraphicsSampler> Sampler) : IShaderData;
