using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Supplies the command and output storage for GPU-generated dispatch.</summary>
public partial record struct IndirectComputeArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="IndirectComputeArguments"/> structure.</summary>
    /// <param name="command">The writable dispatch command.</param>
    /// <param name="output">The writable execution result.</param>
    public IndirectComputeArguments(IGpuRef<DispatchIndirectArguments> command, IGpuRef<uint> output)
    {
        _data = default;
        Command = command;
        Output = output;
    }

    /// <summary>Gets or sets the shader Command value.</summary>
    public IGpuRef<DispatchIndirectArguments> Command { get; set; }

    /// <summary>Gets or sets the shader Output value.</summary>
    public IGpuRef<uint> Output { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Command;
        public ShaderReferenceSlot Output;
    }
}
