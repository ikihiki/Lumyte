using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class BufferTextureCommand(PlanBuffer<byte> buffer, PlanRange bufferRange, uint bytesPerRow, uint rowsPerImage, PlanTexture texture, PlanTextureRegion region, bool upload) : PlanCommand
{
    private BufferTextureCopyLayout? _layout;
    private TextureCopyRegion? _nativeRegion;

    internal override void Prepare(PreparedResources resources, ArgumentTablePlan tables)
    {
        _layout = new()
        {
            Buffer = resources.Buffer(buffer, bufferRange),
            BytesPerRow = bytesPerRow,
            RowsPerImage = rowsPerImage,
        };
        _nativeRegion = region.Resolve(texture, resources);
    }

    internal override void Record(IGraphicsCommandBuffer commands)
    {
        if (upload)
        {
            commands.CopyBufferToTexture(_layout!, _nativeRegion!);
        }
        else
        {
            commands.CopyTextureToBuffer(_nativeRegion!, _layout!);
        }
    }
}
