using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed record class ConsumerLink(IGpuRef<IGraphicsTextureView> Texture) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerLink";

    public static int TransferSize => 16;

    public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Texture", 0, 16, typeof(ShaderReferenceSlot), true)];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter
    {
        Wire wire = default;
        var emitter = new Emitter(Texture);
        ShaderWire.Write(in wire, ref emitter, ref writer);
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Wire
    {
        [FieldOffset(0)]
        public ShaderReferenceSlot Texture;
    }

    private readonly struct Emitter(IGpuRef<IGraphicsTextureView> texture) : IShaderReferenceEmitter
    {
        public void Emit<TCursor>(ref TCursor cursor)
            where TCursor : IShaderReferenceCursor, allows ref struct
            => cursor.WriteReference(0, texture);
    }
}
