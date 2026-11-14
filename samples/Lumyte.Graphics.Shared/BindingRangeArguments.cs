using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Selects elements from independently registered buffer ranges.</summary>
public partial record struct BindingRangeArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="BindingRangeArguments"/> structure.</summary>
    /// <param name="first">The first selected element.</param>
    /// <param name="second">The second selected element.</param>
    /// <param name="output">The writable result.</param>
    public BindingRangeArguments(IGpuRef<uint> first, IGpuRef<uint> second, IGpuRef<uint> output)
    {
        _data = default;
        First = first;
        Second = second;
        Output = output;
    }

    /// <summary>Gets or sets the shader First value.</summary>
    public IGpuRef<uint> First { get; set; }

    /// <summary>Gets or sets the shader Second value.</summary>
    public IGpuRef<uint> Second { get; set; }

    /// <summary>Gets or sets the shader Output value.</summary>
    public IGpuRef<uint> Output { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot First;
        public ShaderReferenceSlot Second;
        public ShaderReferenceSlot Output;
    }
}
