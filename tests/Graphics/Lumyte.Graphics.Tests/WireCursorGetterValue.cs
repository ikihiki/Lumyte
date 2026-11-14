using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial struct WireCursorGetterValue : IShaderArguments
{
    private readonly System.Runtime.CompilerServices.StrongBox<int> _counter = new();
    private Storage _data;

    public WireCursorGetterValue()
    {
    }

    public partial uint Number { readonly get; set; }

    public readonly IGpuRef<uint>? A
    {
        get
        {
            _counter.Value++;
            return (IGpuRef<uint>?)Reference;
        }
    }

    internal object? Reference { get; set; }

    internal int GetterCalls => _counter.Value;

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct Storage
    {
        [FieldOffset(0)]
        public uint Number;

        [FieldOffset(8)]
        public ShaderReferenceSlot A;
    }
}
