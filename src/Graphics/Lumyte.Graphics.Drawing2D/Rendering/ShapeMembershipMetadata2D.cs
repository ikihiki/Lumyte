using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal partial record struct ShapeMembershipMetadata2D : IShaderArguments
{
    private TransferData _data;

    public ShapeMembershipMetadata2D()
    {
        _data = default;
    }

    public IGpuRef<DrawDirectoryEntry2D> PaintDirectory { get; set; } = null!;

    public IGpuRef<DrawDirectoryEntry2D> ClipDirectory { get; set; } = null!;

    public IGpuRef<Vector4> NumericPayload { get; set; } = null!;

    public IGpuRef<MaskDescriptor2D> MaskDescriptors { get; set; } = null!;

    public IGpuRef<ImagePaintData2D> ImagePaintData { get; set; } = null!;

    public IGpuRef<uint> ShapeMembership { get; set; } = null!;

    static string IShaderArguments.ShaderTypeName => "DrawPageMetadata2D";

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot PaintDirectory;
        public ShaderReferenceSlot ClipDirectory;
        public ShaderReferenceSlot NumericPayload;
        public ShaderReferenceSlot MaskDescriptors;
        public ShaderReferenceSlot ImagePaintData;
        public ShaderReferenceSlot ShapeMembership;
    }
}
