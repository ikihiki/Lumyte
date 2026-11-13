using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial class ExplicitTransferRoot : IShaderArguments
{
    private Storage _data = default;

    public IGpuRef<uint>? Numeric { get; set; }

    public IGpuRef<ExplicitTransferValue>? Typed { get; set; }

    public IGpuRef<uint>? Output { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Storage
    {
        public ShaderReferenceSlot Numeric;
        public ShaderReferenceSlot Typed;
        public ShaderReferenceSlot Output;
    }
}
