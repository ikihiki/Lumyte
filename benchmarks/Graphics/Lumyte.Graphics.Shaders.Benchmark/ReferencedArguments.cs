using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shaders.Benchmark;

/// <summary>Nested bulk numeric ranges plus two independent reference fixups.</summary>
public partial struct ReferencedArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Gets or sets the nested numerical transfer.</summary>
    public partial NumericArguments Numeric { get; set; }

    /// <summary>Gets or sets the first buffer range.</summary>
    public IGpuRef<uint>? First { get; set; }

    /// <summary>Gets or sets the second buffer range, with a different offset.</summary>
    public IGpuRef<uint>? Second { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public NumericArguments Numeric;
        public ShaderReferenceSlot First;
        public ShaderReferenceSlot Second;
    }
}
