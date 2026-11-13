using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Defines application root values selecting a logical material range.</summary>
public sealed partial record class BindingDrawArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="BindingDrawArguments"/> class.</summary>
    /// <param name="materials">The logical material range.</param>
    /// <param name="count">The number of materials drawn by the shader.</param>
    public BindingDrawArguments(IGpuRef<BindingMaterial> materials, uint count)
    {
        _data = new()
        {
            Count = count,
        };

        Materials = materials;
    }

    /// <summary>Gets or sets the shader Materials value.</summary>
    public IGpuRef<BindingMaterial> Materials { get; set; }

    /// <summary>Gets or sets the shader Count value.</summary>
    public partial uint Count { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Materials;
        public uint Count;
    }
}
