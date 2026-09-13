using P = Lumyte.Graphics.Portable;
using Fixture = Lumyte.Graphics.WebGPU.Tests.WebGpuPortableRasterFixture;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableRasterPipelineTests
{
    [Fact]
    public async Task ManagedEncodingFailureReleasesPreviouslyAcquiredAttachmentViews()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuBufferHandle indices = fixture.Buffer(4, P.GpuBufferUsage.Index);
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline();
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(target, new(0, 0, 1, 1))]);
        commands.SetPipeline(pipeline);
        commands.DrawIndexed(new(indices), (P.GpuIndexFormat)int.MaxValue, 1);
        commands.EndRendering();
        P.GpuSemaphore completion = fixture.Semaphore();

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            fixture.Queue.Submit([commands], completion, 1));
        Assert.Equal("format", failure.ParamName);
        Assert.Equal(0, fixture.Backend.CacheStatistics.ViewEntries);
        P.GpuCommandBuffer read = fixture.Record();
        P.GpuBufferHandle readback = fixture.Readback(read, target);
        fixture.Queue.Submit([read], completion, 1);
        await fixture.Queue.WaitAsync(completion, 1);

        Assert.Equal(Fixture.Pixel.Transparent, await fixture.PixelAsync(readback));
        Assert.Equal(0, fixture.Backend.PendingCommandCount);
    }

    [Fact]
    public async Task InvalidFixedStateIsDiagnosedByTheRuntimeWhenItsDrawIsSubmitted()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(description:
            new P.GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]) { SampleCount = 3 });
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(target)]);
        commands.SetPipeline(pipeline);
        commands.Draw(3);
        commands.EndRendering();
        P.GpuSemaphore completion = fixture.Semaphore();

        fixture.Queue.Submit([commands], completion, 1);
        P.GpuExecutionException failure = await Assert.ThrowsAsync<P.GpuExecutionException>(async () =>
            await fixture.Queue.WaitAsync(completion, 1));

        Assert.NotEmpty(failure.Diagnostics);
        Assert.True(fixture.Queue.IsComplete(completion, 1));
        Assert.Equal(0, fixture.Backend.CacheStatistics.ViewEntries);
    }

    [Fact]
    public async Task OnlyPipelinesUsedBySubmittedDrawsAreCreatedAndReused()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuRasterPipelineHandle unused = fixture.Pipeline(vertex: "missing");
        P.GpuRasterPipelineHandle used = fixture.Pipeline();
        P.GpuCommandBuffer clear = fixture.Record();
        clear.BeginRendering([Fixture.Color(target)]);
        clear.SetPipeline(unused);
        clear.EndRendering();
        await fixture.SubmitAndWaitAsync(clear);
        Assert.Equal(0, fixture.Backend.RasterPipelineStatistics.Creations);
        P.GpuCommandBuffer first = fixture.Record();
        first.BeginRendering([Fixture.Color(target)]);
        first.SetPipeline(unused);
        first.SetPipeline(used);
        first.Draw(3);
        first.EndRendering();
        Assert.Equal(0, fixture.Backend.RasterPipelineStatistics.Creations);
        await fixture.SubmitAndWaitAsync(first);
        Assert.Equal(1, fixture.Backend.RasterPipelineStatistics.Creations);

        P.GpuCommandBuffer second = fixture.Record();
        second.BeginRendering([Fixture.Color(target)]);
        second.SetPipeline(used);
        second.Draw(3);
        second.EndRendering();
        await fixture.SubmitAndWaitAsync(second);

        Assert.Equal(1, fixture.Backend.RasterPipelineStatistics.Creations);
        Assert.Equal(0, fixture.Backend.CacheStatistics.ViewEntries);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShaderAndPipelineDiagnosticsBelongToTheCompletedRasterSubmission(bool invalidModule)
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(invalidModule ? "invalid WGSL" : Fixture.SolidShader,
            vertex: invalidModule ? "vertex" : "missing");
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(target)]);
        commands.SetPipeline(pipeline);
        commands.Draw(3);
        commands.EndRendering();
        P.GpuSemaphore completion = fixture.Semaphore();

        fixture.Queue.Submit([commands], completion, 1);
        P.GpuExecutionException failure = await Assert.ThrowsAsync<P.GpuExecutionException>(async () =>
            await fixture.Queue.WaitAsync(completion, 1));

        Assert.Equal(new P.GpuFenceValue(completion, 1), failure.FenceValue);
        Assert.NotEmpty(failure.Diagnostics);
        Assert.True(fixture.Queue.IsComplete(completion, 1));
        Assert.Equal(0, fixture.Backend.PendingCommandCount);
        Assert.Equal(0, fixture.Backend.CacheStatistics.ViewEntries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(12)]
    public async Task WrongRootSizesRejectTheWholeSubmissionBeforeAttachmentWork(int rootSize)
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(Fixture.RootShader, immediateSize: 8);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(target, new(0, 0, 1, 1))]);
        commands.SetPipeline(pipeline);
        commands.SetRootData(new Fixture.Root(0xff0000ff));
        commands.Draw(3);
        commands.SetRootData(new byte[rootSize]);
        commands.Draw(3);
        commands.EndRendering();
        P.GpuSemaphore completion = fixture.Semaphore();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            fixture.Queue.Submit([commands], completion, 1));
        Assert.Contains("complete root byte sequence", failure.Message);
        P.GpuCommandBuffer read = fixture.Record();
        P.GpuBufferHandle readback = fixture.Readback(read, target);
        fixture.Queue.Submit([read], completion, 1);
        await fixture.Queue.WaitAsync(completion, 1);

        Assert.Equal(Fixture.Pixel.Transparent, await fixture.PixelAsync(readback));
        Assert.Equal(0, fixture.Backend.PendingCommandCount);
        Assert.Equal(0, fixture.Backend.CacheStatistics.ViewEntries);
    }

    [Fact]
    public async Task InvalidAttachmentDiagnosticsIncludeItsTextureCreationFailure()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle invalid = fixture.Texture(width: 0);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(invalid)]);
        commands.EndRendering();
        P.GpuSemaphore completion = fixture.Semaphore();

        fixture.Queue.Submit([commands], completion, 1);
        P.GpuExecutionException failure = await Assert.ThrowsAsync<P.GpuExecutionException>(async () =>
            await fixture.Queue.WaitAsync(completion, 1));

        IReadOnlyList<P.GpuDiagnostic> textureDiagnostics = await fixture.Backend.GetCreationDiagnostics(invalid);
        Assert.NotEmpty(textureDiagnostics);
        Assert.All(textureDiagnostics, item => Assert.Contains(item, failure.Diagnostics));
        Assert.Equal(0, fixture.Backend.CacheStatistics.ViewEntries);
    }
}
