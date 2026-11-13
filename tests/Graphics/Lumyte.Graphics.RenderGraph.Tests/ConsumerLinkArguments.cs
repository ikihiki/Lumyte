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
        var wire = new Wire { Float = Float };
        var emitter = new Emitter(Data);
        ShaderWire.Write(in wire, ref emitter, ref writer);
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct Wire
    {
        [FieldOffset(0)]
        public ShaderReferenceSlot Data;

        [FieldOffset(16)]
        public float Float;
    }

    private readonly struct Emitter(IGpuRef<ConsumerLink> data) : IShaderReferenceEmitter
    {
        public void Emit<TCursor>(ref TCursor cursor)
            where TCursor : IShaderReferenceCursor, allows ref struct
            => cursor.WriteReference(0, data);
    }
}
