using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial struct WireCursorOverlapGetterValue(IGpuRef<uint> a, IGpuRef<uint> b) : IShaderArguments
{
    private readonly System.Runtime.CompilerServices.StrongBox<int> _a = new();
    private readonly System.Runtime.CompilerServices.StrongBox<int> _b = new();
    private Storage _data = default;

    public readonly IGpuRef<uint> A
    {
        get
        {
            _a.Value++;
            return a;
        }
    }

    public readonly IGpuRef<uint> B
    {
        get
        {
            _b.Value++;
            return b;
        }
    }

    internal int AGetterCalls => _a.Value;

    internal int BGetterCalls => _b.Value;

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct Storage
    {
        [FieldOffset(8)]
        public ShaderReferenceSlot A;

        [FieldOffset(16)]
        public ShaderReferenceSlot B;
    }
}
