using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial class WireCursorGetterValue : IShaderArguments
{
    private Storage _data;

    public partial uint Number { get; set; }

    public IGpuRef<uint>? A
    {
        get
        {
            GetterCalls++;
            Number = 999;
            return (IGpuRef<uint>?)Reference;
        }
    }

    internal object? Reference { get; set; }

    internal int GetterCalls { get; private set; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct Storage
    {
        [FieldOffset(0)]
        public uint Number;

        [FieldOffset(8)]
        public ShaderReferenceSlot A;
    }
}
