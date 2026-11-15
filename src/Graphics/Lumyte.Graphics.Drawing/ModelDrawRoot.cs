using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines the small native root pointing at explicitly uploaded model draw data.</summary>
/// <remarks>The referenced buffer, its nested resources and argument table must remain alive until GPU completion.</remarks>
public partial record struct ModelDrawRoot : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="ModelDrawRoot"/> structure.</summary>
    /// <param name="draw">The registered range whose first element contains this draw's data.</param>
    public ModelDrawRoot(IGpuRef<ModelDrawArguments> draw)
    {
        _data = default;
        Draw = draw;
    }

    /// <summary>Gets or sets the explicitly uploaded model draw data.</summary>
    public IGpuRef<ModelDrawArguments> Draw { get; set; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Draw;
    }
}
