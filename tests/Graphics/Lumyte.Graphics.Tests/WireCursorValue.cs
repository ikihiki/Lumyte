using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial struct WireCursorValue : IShaderArguments
{
    private Storage _data;

    internal WireCursorValue(byte seed)
    {
        Span<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref _data, 1));
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = unchecked((byte)(seed + i));
        }
    }

    // Deliberately opposite to physical slot order.
    public IGpuRef<uint>? B { get; set; }

    public IGpuRef<uint>? A { get; set; }

    public partial uint Number { readonly get; set; }

    internal byte[] RawBytes() => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in _data, 1)).ToArray();

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
