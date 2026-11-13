using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed record class ConsumerLinkArguments(IGpuRef<ConsumerLink> Data, float Float) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerLinkRoot";

    public static int TransferSize => 32;

    public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Data", 0, 16, typeof(ShaderReferenceSlot), true), new("Float", 16, 4, typeof(float))];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter
    {
        Wire wire = new() { Float = Float };
        writer.WriteBytes(in wire);
        writer.WriteReference("Data", 0, Data);
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
