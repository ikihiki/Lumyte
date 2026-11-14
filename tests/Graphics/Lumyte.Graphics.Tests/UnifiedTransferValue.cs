using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

// One generated provider is both a uniform root and a typed-buffer element.
internal partial struct UnifiedTransferValue : IShaderArguments
{
    private Storage _data;

    public partial uint Value { readonly get; set; }

    public IGpuRef<uint>? Output { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Storage
    {
        public uint Value;
        public ShaderReferenceSlot Output;
    }
}
