using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Defines application-owned linked shader data for dependency traversal checks.</summary>
public sealed partial record class BindingNode : IShaderData
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="BindingNode"/> class.</summary>
    /// <param name="value">The numeric payload.</param>
    /// <param name="next">The next logical element, which may form a cycle.</param>
    public BindingNode(uint value, IGpuRef<BindingNode> next)
    {
        _data = new()
        {
            Value = value,
        };

        Next = next;
    }

    /// <summary>Gets or sets the shader Value value.</summary>
    public partial uint Value { get; set; }

    /// <summary>Gets or sets the shader Next value.</summary>
    public IGpuRef<BindingNode> Next { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public uint Value;
        public ShaderReferenceSlot Next;
    }
}
