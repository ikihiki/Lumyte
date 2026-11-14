using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

// Deliberately declare B before A: the generated cursor must evaluate physical order.
internal partial class WireCursorOrderedGetterValue(IGpuRef<uint> a, IGpuRef<uint> b, List<string> events) : IShaderArguments
{
    private Storage _data;

    public partial uint Number { get; set; }

    public IGpuRef<uint> B
    {
        get
        {
            events.Add("B");
            Number = 999;
            return b;
        }
    }

    public IGpuRef<uint> A
    {
        get
        {
            events.Add("A");
            Number = 777;
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
