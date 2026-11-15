using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Exercises actual decorator copy footprints rather than caller slice capacity.</summary>
public sealed class BufferTextureFootprintTests
{
    /// <summary>Both directions include intermediate padding but exclude final row padding.</summary>
    /// <param name="upload">Whether the buffer is the source.</param>
    /// <param name="layers">The copied layer count.</param>
    /// <param name="offset">The later byte access.</param>
    /// <param name="overlaps">Whether that byte is inside the contiguous footprint.</param>
    [Theory]
    [InlineData(true, 1U, 0UL, false)]
    [InlineData(false, 1U, 0UL, false)]
    [InlineData(true, 1U, 8UL, true)]
    [InlineData(false, 1U, 8UL, true)]
    [InlineData(true, 1U, 20UL, true)]
    [InlineData(false, 1U, 20UL, true)]
    [InlineData(true, 1U, 31UL, true)]
    [InlineData(false, 1U, 31UL, true)]
    [InlineData(true, 1U, 32UL, false)]
    [InlineData(false, 1U, 32UL, false)]
    [InlineData(true, 1U, 128UL, false)]
    [InlineData(false, 1U, 128UL, false)]
    [InlineData(true, 2U, 40UL, true)]
    [InlineData(false, 2U, 40UL, true)]
    [InlineData(true, 2U, 95UL, true)]
    [InlineData(false, 2U, 95UL, true)]
    [InlineData(true, 2U, 96UL, false)]
    [InlineData(false, 2U, 96UL, false)]
    public void CopyTracksOnlyContiguousFootprint(bool upload, uint layers, ulong offset, bool overlaps)
    {
        var backend = new FootprintBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsBuffer<byte> buffer = Buffer(device);
        object? rawBuffer = backend.LastBuffer;
        using IGraphicsBuffer<byte> other = Buffer(device);
        using IGraphicsTexture texture = Texture(device, layers);
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        Transition(command, texture, upload, layers);
        var layout = new BufferTextureCopyLayout { Buffer = buffer.SliceElements(8, 256), BytesPerRow = 16, RowsPerImage = 4 };
        var region = new TextureCopyRegion { Texture = texture, Width = 2, Height = 2, ArrayLayerCount = layers };
        Copy(command, layout, region, upload);
        Assert.NotNull(backend.Command.LastLayout);
        Assert.Same(rawBuffer, backend.Command.LastLayout.Buffer.Buffer);
        Assert.Equal(8UL, backend.Command.LastLayout.Buffer.OffsetInBytes);
        Assert.Equal(256UL, backend.Command.LastLayout.Buffer.SizeInBytes);
        Action access = upload
            ? () => command.CopyBuffer(other.SliceElements(0, 1), buffer.SliceElements(offset, 1))
            : () => command.CopyBuffer(buffer.SliceElements(offset, 1), other.SliceElements(0, 1));
        if (overlaps)
        {
            Assert.Throws<InvalidOperationException>(access);
        }
        else
        {
            access();
        }
    }

    /// <summary>Native failures and arithmetic failures leave no phantom buffer accesses.</summary>
    /// <param name="upload">Whether the buffer is the source.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedCopiesRollBackAndFootprintArithmeticIsChecked(bool upload)
    {
        var backend = new FootprintBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsBuffer<byte> buffer = Buffer(device);
        using IGraphicsBuffer<byte> other = Buffer(device);
        using IGraphicsTexture texture = Texture(device, 1);
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        Transition(command, texture, upload, 1);
        var layout = new BufferTextureCopyLayout { Buffer = buffer.SliceElements(8, 256), BytesPerRow = 16, RowsPerImage = 4 };
        var region = new TextureCopyRegion { Texture = texture, Width = 2, Height = 2 };
        Assert.Throws<OverflowException>(() => Copy(command, layout with { BytesPerRow = uint.MaxValue, RowsPerImage = uint.MaxValue }, region with { ArrayLayerCount = uint.MaxValue }, upload));
        Assert.Null(backend.Command.LastLayout);
        Assert.Throws<ArgumentOutOfRangeException>(() => Copy(command, layout, region with { Height = 0 }, upload));
        Assert.Throws<ArgumentOutOfRangeException>(() => Copy(command, layout, region with { Width = 0 }, upload));
        Assert.Throws<ArgumentOutOfRangeException>(() => Copy(command, layout, region with { ArrayLayerCount = 0 }, upload));
        backend.Command.FailCopy = true;
        Assert.Throws<InvalidOperationException>(() => Copy(command, layout, region, upload));
        if (upload)
        {
            command.CopyBuffer(other.SliceElements(0, 1), buffer.SliceElements(8, 1));
        }
        else
        {
            command.CopyBuffer(buffer.SliceElements(8, 1), other.SliceElements(0, 1));
        }
    }

    private static IGraphicsBuffer<byte> Buffer(ValidationDevice device) => device.CreateBuffer(new BufferDesc<byte> { Count = 512, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });

    private static IGraphicsTexture Texture(ValidationDevice device, uint layers) => device.CreateTexture(new TextureDesc { Width = 2, Height = 2, ArrayLayers = layers, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopySource | TextureUsage.CopyDestination });

    private static void Transition(IGraphicsCommandBuffer command, IGraphicsTexture texture, bool upload, uint layers) => command.Barrier(new TextureBarrierDesc
    {
        Texture = texture,
        Range = new(0, 1, 0, layers),
        BeforeState = TextureState.Undefined,
        AfterState = upload ? TextureState.CopyDestination : TextureState.CopySource,
        Before = default,
        After = new(PipelineStage.Copy, upload ? ResourceAccess.CopyWrite : ResourceAccess.CopyRead),
    });

    private static void Copy(IGraphicsCommandBuffer command, BufferTextureCopyLayout layout, TextureCopyRegion region, bool upload)
    {
        if (upload)
        {
            command.CopyBufferToTexture(layout, region);
        }
        else
        {
            command.CopyTextureToBuffer(region, layout);
        }
    }
}
