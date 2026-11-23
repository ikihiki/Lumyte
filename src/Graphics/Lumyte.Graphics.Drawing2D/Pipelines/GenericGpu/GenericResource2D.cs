using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal partial record struct GenericResource2D : IShaderArguments
{
    private TransferData _data = default;

    public GenericResource2D()
    {
    }

    public IGpuRef<IGraphicsTextureView> Texture { get; set; } = null!;

    public IGpuRef<IGraphicsSampler> Sampler { get; set; } = null!;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Texture;
        public ShaderReferenceSlot Sampler;
    }
}
