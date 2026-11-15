using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal partial record struct SolidRectangleArguments2D : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="SolidRectangleArguments2D"/> structure.</summary>
    public SolidRectangleArguments2D(IGpuRef<Vector4> data, Vector2 size)
    {
        _data = new()
        {
            Size = size,
        };

        Data = data;
    }

    /// <summary>Gets or sets the shader Data value.</summary>
    public IGpuRef<Vector4> Data { get; set; }

    /// <summary>Gets or sets the shader Size value.</summary>
    public partial Vector2 Size { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Data;
        public Vector2 Size;
    }
}
