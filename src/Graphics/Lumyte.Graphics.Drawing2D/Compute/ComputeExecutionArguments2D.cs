using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

// Deliberately independent from DrawRangeArguments2D: compute has writable storage,
// whereas the graphics resolve has a separate read-only table and no paint bindings.
internal partial record struct ComputeExecutionArguments2D : IShaderArguments
{
    private TransferData _data;

    public ComputeExecutionArguments2D()
    {
        _data = default;
    }

    public IGpuRef<DrawRecord2D> Records { get; set; } = null!;

    public IGpuRef<DrawDirectoryEntry2D> PaintDirectory { get; set; } = null!;

    public IGpuRef<MaskDescriptor2D> MaskDescriptors { get; set; } = null!;

    public IGpuRef<ImagePaintData2D> ImagePaintData { get; set; } = null!;

    public IGpuRef<Vector4> Input { get; set; } = null!;

    public IGpuRef<Vector4> Storage { get; set; } = null!;

    public IGpuRef<uint> Work { get; set; } = null!;

    public partial uint Stage { get; set; }

    public partial uint ClipOffset { get; set; }

    public partial uint NumericOffset { get; set; }

    public partial uint Reserved2 { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Records;
        public ShaderReferenceSlot PaintDirectory;
        public ShaderReferenceSlot MaskDescriptors;
        public ShaderReferenceSlot ImagePaintData;
        public ShaderReferenceSlot Input;
        public ShaderReferenceSlot Storage;
        public ShaderReferenceSlot Work;
        public uint Stage;
        public uint ClipOffset;
        public uint NumericOffset;
        public uint Reserved2;
    }
}

internal partial record struct ComputeExecutionResolveArguments2D : IShaderArguments
{
    private TransferData _data;

    public ComputeExecutionResolveArguments2D()
    {
        _data = default;
    }

    public IGpuRef<Vector4> Color { get; set; } = null!;

    public partial uint Width { get; set; }

    public partial uint Height { get; set; }

    public partial uint Reserved0 { get; set; }

    public partial uint Reserved1 { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Color;
        public uint Width;
        public uint Height;
        public uint Reserved0;
        public uint Reserved1;
    }
}
