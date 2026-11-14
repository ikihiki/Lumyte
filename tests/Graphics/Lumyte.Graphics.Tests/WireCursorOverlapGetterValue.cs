using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial class WireCursorOverlapGetterValue(IGpuRef<uint> a, IGpuRef<uint> b) : IShaderArguments
{
    private Storage _data = default;

    public IGpuRef<uint> A
    {
        get
        {
            AGetterCalls++;
            return a;
        }
    }

    public IGpuRef<uint> B
    {
        get
        {
            BGetterCalls++;
            return b;
        }
    }

    internal int AGetterCalls { get; private set; }

    internal int BGetterCalls { get; private set; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct Storage
    {
        [FieldOffset(8)]
        public ShaderReferenceSlot A;

        [FieldOffset(16)]
        public ShaderReferenceSlot B;
    }
}
