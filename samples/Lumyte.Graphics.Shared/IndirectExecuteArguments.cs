using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Supplies writable results for the indirect compute consumer.</summary>
public partial record struct IndirectExecuteArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="IndirectExecuteArguments"/> structure.</summary>
    /// <param name="output">The writable execution result.</param>
    public IndirectExecuteArguments(IGpuRef<uint> output)
    {
        _data = default;
        Output = output;
    }

    /// <summary>Gets or sets the shader Output value.</summary>
    public IGpuRef<uint> Output { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Output;
    }
}
