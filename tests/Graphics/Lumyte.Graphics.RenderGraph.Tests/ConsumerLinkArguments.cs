using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal record struct ConsumerLinkArguments(IGpuRef<ConsumerLink> Data, float Float) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerLinkRoot";

    public static int TransferSize => 32;

    public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Data", 0, 16, typeof(ShaderReferenceSlot), true), new("Float", 16, 4, typeof(float))];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        var wire = new Wire { Float = Float };
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
        writer.WriteReference(Data);
        writer.WriteValue(bytes[16..]);
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct Wire
    {
        [FieldOffset(0)]
        public ShaderReferenceSlot Data;

        [FieldOffset(16)]
        public float Float;
    }
}
