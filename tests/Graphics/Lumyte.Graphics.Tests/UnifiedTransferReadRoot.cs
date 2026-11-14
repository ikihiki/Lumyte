using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial struct UnifiedTransferReadRoot : IShaderArguments
{
    private Storage _data = default;

    public UnifiedTransferReadRoot()
    {
    }

    public IGpuRef<UnifiedTransferValue>? Values { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Storage
    {
        public ShaderReferenceSlot Values;
    }
}
