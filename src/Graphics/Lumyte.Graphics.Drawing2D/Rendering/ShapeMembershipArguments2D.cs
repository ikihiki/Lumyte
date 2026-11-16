using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal partial record struct ShapeMembershipArguments2D : IShaderArguments
{
    private TransferData _data;

    public ShapeMembershipArguments2D()
    {
        _data = default;
    }

    public IGpuRef<DrawRecord2D> Records { get; set; } = null!;

    public IGpuRef<Vector4> Numeric { get; set; } = null!;

    public IGpuRef<uint> Output { get; set; } = null!;

    public partial uint RecordIndex { get; set; }

    public partial uint Width { get; set; }

    public partial uint Height { get; set; }

    public partial uint Offset { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Records;
        public ShaderReferenceSlot Numeric;
        public ShaderReferenceSlot Output;
        public uint RecordIndex;
        public uint Width;
        public uint Height;
        public uint Offset;
    }
}
