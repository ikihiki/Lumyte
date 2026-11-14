using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Defines application root values for compute checks.</summary>
public partial record struct BindingComputeArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="BindingComputeArguments"/> structure.</summary>
    /// <param name="camera">The row-major camera matrix.</param>
    /// <param name="factor">The integer payload.</param>
    /// <param name="node">The first linked data element.</param>
    /// <param name="output">The writable raw output range.</param>
    public BindingComputeArguments(Matrix4x4 camera, uint factor, IGpuRef<BindingNode> node, IGpuRef<uint> output)
    {
        _data = new()
        {
            Camera = camera,
            Factor = factor,
        };

        Node = node;
        Output = output;
    }

    /// <summary>Gets or sets the shader Camera value.</summary>
    public partial Matrix4x4 Camera { get; set; }

    /// <summary>Gets or sets the shader Factor value.</summary>
    public partial uint Factor { get; set; }

    /// <summary>Gets or sets the shader Node value.</summary>
    public IGpuRef<BindingNode> Node { get; set; }

    /// <summary>Gets or sets the shader Output value.</summary>
    public IGpuRef<uint> Output { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public Matrix4x4 Camera;
        public uint Factor;
        public ShaderReferenceSlot Node;
        public ShaderReferenceSlot Output;
    }
}
