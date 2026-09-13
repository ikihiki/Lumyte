using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableTextureCopyTests
{
    [Theory]
    [InlineData(P.GpuTextureDimension.Texture2D)]
    [InlineData(P.GpuTextureDimension.Texture3D)]
    public async Task PaddedCopiesPreserveMipRegionsAcrossTextureCopies(P.GpuTextureDimension dimension)
    {
        using TextureFixture fixture = await TextureFixture.CreateAsync();
        P.GpuTextureHandle source = fixture.Texture(dimension);
        P.GpuTextureHandle destination = fixture.Texture(dimension);
        var uploadFootprint = new P.GpuTextureCopyFootprint(1, P.GpuTextureAspect.All,
            new(1, 2, 1), new(3, 2, 2), 256, 768);
        var readFootprint = uploadFootprint with { Mip = 0, Origin = new(4, 3, 0), ImagePitch = 1024 };
        byte[] uploadBytes = Pattern(uploadFootprint, 16);
        P.GpuBufferHandle upload = await fixture.UploadAsync(uploadBytes);
        byte[] expected = Pattern(readFootprint, 32);
        P.GpuBufferHandle readback = fixture.Compute.Buffer((ulong)expected.Length,
            P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer commands = fixture.Record();

        commands.CopyBufferToTexture(new(upload, 16, uploadFootprint.RequiredBytes(GpuFormat.Rgba8Unorm)), source, uploadFootprint);
        commands.CopyTexture(source, uploadFootprint, destination, readFootprint);
        commands.CopyTextureToBuffer(destination, readFootprint, new(readback, 32, readFootprint.RequiredBytes(GpuFormat.Rgba8Unorm)));
        await fixture.Compute.SubmitAndWaitAsync(commands);

        Assert.Equal(expected, await fixture.ReadAsync(readback, expected.Length));
    }

    [Fact]
    public async Task SingleRowCopyAllowsOmittedStrides()
    {
        using TextureFixture fixture = await TextureFixture.CreateAsync();
        P.GpuTextureHandle texture = fixture.Texture();
        var footprint = new P.GpuTextureCopyFootprint(0, P.GpuTextureAspect.All, new(2, 3, 0), new(4, 1, 1));
        byte[] expected = Enumerable.Range(1, 16).Select(static value => (byte)value).ToArray();
        P.GpuBufferHandle upload = await fixture.UploadAsync(expected);
        P.GpuBufferHandle readback = fixture.Compute.Buffer(16, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer commands = fixture.Record();

        commands.CopyBufferToTexture(new(upload), texture, footprint);
        commands.CopyTextureToBuffer(texture, footprint, new(readback));
        await fixture.Compute.SubmitAndWaitAsync(commands);

        Assert.Equal(expected, await fixture.ReadAsync(readback, expected.Length));
    }

    [Fact]
    public async Task StencilAspectCopiesWithoutAHostDepthRepresentation()
    {
        using TextureFixture fixture = await TextureFixture.CreateAsync();
        P.GpuTextureHandle texture = fixture.Texture(format: GpuFormat.Depth24PlusStencil8);
        var footprint = new P.GpuTextureCopyFootprint(0, P.GpuTextureAspect.StencilOnly, default, new(16, 16, 1), 256);
        byte[] expected = new byte[4096];
        for (int row = 0; row < 16; row++)
        { for (int column = 0; column < 16; column++) { expected[row * 256 + column] = (byte)(row * 16 + column); } }
        P.GpuBufferHandle upload = await fixture.UploadAsync(expected);
        P.GpuBufferHandle readback = fixture.Compute.Buffer((ulong)expected.Length,
            P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer commands = fixture.Record();

        commands.CopyBufferToTexture(new(upload), texture, footprint);
        commands.CopyTextureToBuffer(texture, footprint, new(readback));
        await fixture.Compute.SubmitAndWaitAsync(commands);

        Assert.Equal(expected, await fixture.ReadAsync(readback, expected.Length));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LogicalBufferRangeMustCoverTheTextureBytes(bool upload)
    {
        using TextureFixture fixture = await TextureFixture.CreateAsync();
        P.GpuTextureHandle texture = fixture.Texture();
        P.GpuBufferHandle buffer = fixture.Compute.Buffer(512, P.GpuBufferUsage.CopySource | P.GpuBufferUsage.CopyDestination);
        var footprint = new P.GpuTextureCopyFootprint(0, P.GpuTextureAspect.All, default, new(4, 2, 1), 256);
        P.GpuCommandBuffer commands = fixture.Record();

        ArgumentOutOfRangeException failure = upload
            ? Assert.Throws<ArgumentOutOfRangeException>(() => commands.CopyBufferToTexture(new(buffer, 0, 271), texture, footprint))
            : Assert.Throws<ArgumentOutOfRangeException>(() => commands.CopyTextureToBuffer(texture, footprint, new(buffer, 0, 271)));

        Assert.Equal(upload ? "source" : "destination", failure.ParamName);
    }

    [Theory]
    [InlineData(0ul, 512ul)]
    [InlineData(256ul, 513ul)]
    public async Task UnrepresentableImagePitchIsRejectedWithoutTruncation(ulong rowPitch, ulong imagePitch)
    {
        using TextureFixture fixture = await TextureFixture.CreateAsync();
        P.GpuTextureHandle texture = fixture.Texture();
        P.GpuBufferHandle buffer = fixture.Compute.Buffer(2048, P.GpuBufferUsage.CopySource);
        var footprint = new P.GpuTextureCopyFootprint(0, P.GpuTextureAspect.All, default, new(4, 2, 1), rowPitch, imagePitch);
        P.GpuCommandBuffer commands = fixture.Record();

        ArgumentException failure = Assert.Throws<ArgumentException>(() => commands.CopyBufferToTexture(new(buffer), texture, footprint));

        Assert.Equal("footprint", failure.ParamName);
    }

    [Theory]
    [InlineData(4294967295ul, 0ul)]
    [InlineData(256ul, 1099511627520ul)]
    public async Task ExplicitPitchCannotBecomeTheNativeUndefinedSentinel(ulong rowPitch, ulong imagePitch)
    {
        using TextureFixture fixture = await TextureFixture.CreateAsync();
        P.GpuTextureHandle texture = fixture.Texture();
        P.GpuBufferHandle buffer = fixture.Compute.Buffer(16, P.GpuBufferUsage.CopySource);
        var footprint = new P.GpuTextureCopyFootprint(0, P.GpuTextureAspect.All, default, new(4, 1, 1), rowPitch, imagePitch);
        P.GpuCommandBuffer commands = fixture.Record();

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => commands.CopyBufferToTexture(new(buffer), texture, footprint));

        Assert.Equal("footprint", failure.ParamName);
    }

    [Fact]
    public async Task CopyRegionsCannotSilentlyDiscardTheDestinationExtent()
    {
        using TextureFixture fixture = await TextureFixture.CreateAsync();
        P.GpuTextureHandle source = fixture.Texture();
        P.GpuTextureHandle destination = fixture.Texture();
        var footprint = new P.GpuTextureCopyFootprint(0, P.GpuTextureAspect.All, default, new(4, 1, 1));
        P.GpuCommandBuffer commands = fixture.Record();

        ArgumentException failure = Assert.Throws<ArgumentException>(() => commands.CopyTexture(source, footprint,
            destination, footprint with { Extent = new(3, 1, 1) }));

        Assert.Equal("destinationFootprint", failure.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeValidationReportsPitchAndDepthCopyErrorsAtCompletion(bool depth)
    {
        using TextureFixture fixture = await TextureFixture.CreateAsync();
        P.GpuTextureHandle texture = fixture.Texture(format: depth ? GpuFormat.Depth24PlusStencil8 : GpuFormat.Rgba8Unorm);
        P.GpuBufferHandle buffer = fixture.Compute.Buffer(4096, P.GpuBufferUsage.CopyDestination);
        var footprint = new P.GpuTextureCopyFootprint(0, depth ? P.GpuTextureAspect.DepthOnly : P.GpuTextureAspect.All,
            default, new(16, 16, 1), depth ? 256ul : 64ul);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyTextureToBuffer(texture, footprint, new(buffer));
        P.GpuSemaphore completion = fixture.Compute.Semaphore();

        fixture.Compute.Queue.Submit([commands], completion, 1);
        P.GpuExecutionException failure = await Assert.ThrowsAsync<P.GpuExecutionException>(async () =>
            await fixture.Compute.Queue.WaitAsync(completion, 1));

        Assert.NotEmpty(failure.Diagnostics);
        Assert.True(fixture.Compute.Queue.IsComplete(completion, 1));
    }

    private static byte[] Pattern(P.GpuTextureCopyFootprint footprint, int offset)
    {
        byte[] bytes = new byte[checked(offset + (int)footprint.RequiredBytes(GpuFormat.Rgba8Unorm))];
        int value = 1;
        for (uint image = 0; image < footprint.Extent.Depth; image++)
        {
            for (uint row = 0; row < footprint.Extent.Height; row++)
            {
                int start = checked(offset + (int)(image * footprint.ImagePitch + row * footprint.RowPitch));
                for (uint columnByte = 0; columnByte < footprint.Extent.Width * 4; columnByte++)
                { bytes[start + columnByte] = (byte)value++; }
            }
        }
        return bytes;
    }

    private sealed class TextureFixture(WebGpuPortableComputeFixture compute) : IDisposable
    {
        private readonly List<P.GpuTextureHandle> textures = [];
        private readonly List<P.GpuCommandBuffer> recordings = [];
        internal WebGpuPortableComputeFixture Compute { get; } = compute;
        internal static async Task<TextureFixture> CreateAsync() => new(await WebGpuPortableComputeFixture.CreateAsync());

        internal P.GpuCommandBuffer Record()
        {
            P.GpuCommandBuffer recording = Compute.Record();
            recordings.Add(recording);
            return recording;
        }

        internal P.GpuTextureHandle Texture(P.GpuTextureDimension dimension = P.GpuTextureDimension.Texture2D,
            GpuFormat format = GpuFormat.Rgba8Unorm)
        {
            bool depth = format == GpuFormat.Depth24PlusStencil8;
            P.GpuTextureHandle texture = Compute.Backend.CreateTexture(new(dimension, 16, 16,
                dimension == P.GpuTextureDimension.Texture3D ? 8u : 1u, depth ? 1u : 2u,
                dimension == P.GpuTextureDimension.Texture3D || depth ? 1u : 4u, 1, format,
                P.GpuTextureUsage.CopySource | P.GpuTextureUsage.CopyDestination));
            textures.Add(texture);
            return texture;
        }

        internal async Task<P.GpuBufferHandle> UploadAsync(byte[] bytes)
        {
            P.GpuBufferHandle buffer = Compute.Buffer((ulong)bytes.Length, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource);
            using P.GpuMappedBufferRange mapped = await Compute.Backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, (ulong)bytes.Length);
            bytes.CopyTo(mapped.Memory.Span);
            return buffer;
        }

        internal async Task<byte[]> ReadAsync(P.GpuBufferHandle buffer, int length)
        {
            using P.GpuMappedBufferRange mapped = await Compute.Backend.MapBufferAsync(buffer, P.GpuMapMode.Read, 0, (ulong)length);
            return mapped.ReadOnlyMemory.ToArray();
        }

        public void Dispose()
        {
            foreach (P.GpuCommandBuffer recording in recordings) { recording.Dispose(); }
            foreach (P.GpuTextureHandle texture in textures) { Compute.Backend.DestroyTexture(texture); }
            Compute.Dispose();
        }
    }
}
