using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Selects shader data arrays with independently checked layouts.</summary>
public partial record struct BindingLayoutArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="BindingLayoutArguments"/> structure.</summary>
    /// <param name="vectors">The three-component vector array.</param>
    /// <param name="packed">The scalar and vector array.</param>
    /// <param name="nested">The nested structure array.</param>
    /// <param name="output">The writable output values.</param>
    public BindingLayoutArguments(IGpuRef<BindingVector> vectors, IGpuRef<BindingPackedVector> packed, IGpuRef<BindingNestedVector> nested, IGpuRef<uint> output)
    {
        _data = default;
        Vectors = vectors;
        Packed = packed;
        Nested = nested;
        Output = output;
    }

    /// <summary>Gets or sets the shader Vectors value.</summary>
    public IGpuRef<BindingVector> Vectors { get; set; }

    /// <summary>Gets or sets the shader Packed value.</summary>
    public IGpuRef<BindingPackedVector> Packed { get; set; }

    /// <summary>Gets or sets the shader Nested value.</summary>
    public IGpuRef<BindingNestedVector> Nested { get; set; }

    /// <summary>Gets or sets the shader Output value.</summary>
    public IGpuRef<uint> Output { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Vectors;
        public ShaderReferenceSlot Packed;
        public ShaderReferenceSlot Nested;
        public ShaderReferenceSlot Output;
    }
}
