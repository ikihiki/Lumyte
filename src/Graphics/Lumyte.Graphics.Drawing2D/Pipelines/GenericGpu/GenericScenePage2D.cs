using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal partial record struct GenericScenePage2D : IShaderArguments
{
    private TransferData _data = default;

    public GenericScenePage2D()
    {
    }

    public IGpuRef<Vector4> Page { get; set; } = null!;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Page;
    }
}
