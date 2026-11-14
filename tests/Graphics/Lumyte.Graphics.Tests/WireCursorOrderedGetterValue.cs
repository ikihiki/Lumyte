using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

// Deliberately declare B before A: the generated cursor must evaluate physical order.
internal partial struct WireCursorOrderedGetterValue(IGpuRef<uint> a, IGpuRef<uint> b, List<string> events) : IShaderArguments
{
    private Storage _data;

    public partial uint Number { readonly get; set; }

    public readonly IGpuRef<uint> B
    {
        get
        {
            events.Add("B");
            return b;
        }
    }

    public readonly IGpuRef<uint> A
    {
        get
        {
            events.Add("A");
            return a;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct Storage
    {
        [FieldOffset(0)]
        public uint Number;

        [FieldOffset(32)]
        public ShaderReferenceSlot B;

        [FieldOffset(8)]
        public ShaderReferenceSlot A;
    }
}
