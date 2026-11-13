using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed record class ConsumerLink(IGpuRef<IGraphicsTextureView> Texture) : IShaderData
{
    public static string ShaderTypeName => "ConsumerLink";

    public static int TransferSize => 16;

    public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Texture", 0, 16, typeof(ShaderReferenceSlot), true)];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter
    {
        Wire wire = default;
        writer.WriteBytes(in wire);
        writer.WriteReference("Texture", 0, Texture);
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Wire
    {
        [FieldOffset(0)]
        public ShaderReferenceSlot Texture;
    }
}
