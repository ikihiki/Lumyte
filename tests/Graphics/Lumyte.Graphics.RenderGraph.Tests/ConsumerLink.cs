using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal record struct ConsumerLink(IGpuRef<IGraphicsTextureView> Texture) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerLink";

    public static int TransferSize => 16;

    public static int ReferenceCount => 1;

    public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Texture", 0, 16, typeof(ShaderReferenceSlot), true)];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct => writer.WriteReference(Texture);
}
