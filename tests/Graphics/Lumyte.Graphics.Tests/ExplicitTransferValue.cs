using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial class ExplicitTransferValue : IShaderData
{
    private Storage _data;

    public partial uint Value { get; set; }

    public IGpuRef<uint>? Target { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Storage
    {
        public uint Value;
        public ShaderReferenceSlot Target;
    }
}
