using System.Numerics;

using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Portable.Passes.Tests;

public sealed class PortableDraw2DPassTests
{
    [Fact]
    public async Task RegistrationDoesNotCreateGpuObjects()
    {
        var backend = new ManagerTestBackend();
        var passes = new PortableRenderPassRegistry().Add2DRendering();
        var provider = new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), passes);

        await using var runtime = await provider.CreateAsync(new());

        Assert.Empty(backend.Created);
    }

    [Fact]
    public async Task EmptyScenePreservesTheInitializedTarget()
    {
        var backend = new ManagerTestBackend();
        var passes = new PortableRenderPassRegistry().AddImageProcessing().Add2DRendering();
        var provider = new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), passes);
        await using var runtime = await provider.CreateAsync(new());
        using var builder = new Draw2DSceneBuilder();
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("color", new(16, 16, GpuFormat.Rgba8Unorm));
        graph.AddClearPass("clear", new(target, TextureClearValue.Color(Vector4.One)));
        graph.Add2DPass("empty", new(builder.Finish(), target));
        graph.MarkOutput(target);

        using var execution = await runtime.SubmitAsync(graph.Compile());
        await execution.WaitForCompletionAsync();

        var attachments = backend.TestQueue.Recordings.SelectMany(r => r.ColorAttachments).ToArray();
        Assert.Collection(attachments,
            clear => Assert.Equal(GpuAttachmentLoadOperation.Clear, clear.LoadOperation),
            draw => Assert.Equal(GpuAttachmentLoadOperation.Load, draw.LoadOperation));
        Assert.Empty(backend.Created.OfType<GpuShaderModuleHandle>());
    }

    [Fact]
    public async Task PendingSceneUploadIsReusedByLaterSubmissions()
    {
        var backend = new ManagerTestBackend();
        backend.TestQueue.AutoComplete = false;
        await using var runtime = await Runtime(backend);
        var plan = Plan(SolidScene(Color.White));

        using var first = await runtime.SubmitAsync(plan);
        using var second = await runtime.SubmitAsync(plan);
        var sceneBuffer = Assert.Single(StorageBuffers(backend));
        Assert.DoesNotContain(sceneBuffer, backend.Destroyed);
        backend.TestQueue.Complete(0);
        backend.TestQueue.Complete(1);
        await first.WaitForCompletionAsync();
        await second.WaitForCompletionAsync();

        Assert.Single(StorageBuffers(backend));
    }

    [Fact]
    public async Task RetainedEditUploadsOnlyTheChangedNode()
    {
        var backend = new ManagerTestBackend();
        await using var runtime = await Runtime(backend);
        var store = new Draw2DSceneStore();
        store.CreateNode(SolidScene(Color.White));
        var changed = store.CreateNode(SolidScene(new Color(1, 0, 0, 1)));
        using (var initial = await runtime.SubmitAsync(Plan(store.Snapshot())))
        { await initial.WaitForCompletionAsync(); }
        int before = StorageBuffers(backend).Count();

        store.SetContent(changed, SolidScene(new Color(0, 1, 0, 1)));
        using var updated = await runtime.SubmitAsync(Plan(store.Snapshot()));
        await updated.WaitForCompletionAsync();

        Assert.Equal(2, before);
        Assert.Equal(before + 1, StorageBuffers(backend).Count());
    }

    [Fact]
    public async Task FailedUploadIsRecreatedBeforeTheNextDraw()
    {
        var backend = new ManagerTestBackend();
        backend.TestQueue.AutoComplete = false;
        await using var runtime = await Runtime(backend);
        var plan = Plan(SolidScene(Color.White));
        using var failed = await runtime.SubmitAsync(plan);
        backend.TestQueue.Complete(0, new GpuDiagnostic(GpuDiagnosticKind.Validation, "scene upload failed"));
        await Assert.ThrowsAnyAsync<Exception>(() => failed.WaitForCompletionAsync().AsTask());

        backend.TestQueue.AutoComplete = true;
        using var retry = await runtime.SubmitAsync(plan);
        await retry.WaitForCompletionAsync();

        var upload = Assert.Single(backend.TestQueue.Submitted[^1].Recordings.SelectMany(r => r.BufferCopies));
        Assert.True(((ManagerTestBackend.Buffer)upload.Destination.Buffer).Description.Usage.HasFlag(GpuBufferUsage.Storage));
    }

    private static ValueTask<IGpuRenderRuntime> Runtime(ManagerTestBackend backend)
        => new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend),
            new PortableRenderPassRegistry().AddImageProcessing().Add2DRendering()).CreateAsync(new());

    [Fact]
    public async Task IndependentScenesReuseTheSameImageRevision()
    {
        var backend = new ManagerTestBackend();
        await using var runtime = await Runtime(backend);
        var image = new GpuImageUploadData(new("shared-pixel", 1), new(1, 1, GpuFormat.Rgba8Unorm),
            GpuImageColorEncoding.Linear, GpuImageAlphaMode.Straight, [new(0, 0, 4, 4, new byte[] { 200, 100, 50, 128 })]);
        using var firstBuilder = new Draw2DSceneBuilder();
        firstBuilder.DrawImage(new(image), new(1, 1, 4, 4));
        using var secondBuilder = new Draw2DSceneBuilder();
        secondBuilder.DrawImage(new(image), new(6, 6, 4, 4));

        using var first = await runtime.SubmitAsync(Plan(firstBuilder.Finish()));
        await first.WaitForCompletionAsync();
        using var second = await runtime.SubmitAsync(Plan(secondBuilder.Finish()));
        await second.WaitForCompletionAsync();

        Assert.Single(backend.Created.OfType<ManagerTestBackend.Texture>(), t =>
            t.Description.Usage == (GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination));
    }

    [Fact]
    public async Task DeviceScaleChangeRepreparesBlurEvenWhenTheWorldTransformMatches()
    {
        var backend = new ManagerTestBackend();
        await using var runtime = await Runtime(backend);
        using var builder = new Draw2DSceneBuilder();
        using (builder.BeginLayer(new(BlurRadius: 3)))
        { builder.FillRectangle(new(2, 2, 8, 8), Brush.Solid(Color.White)); }
        var store = new Draw2DSceneStore();
        var node = store.CreateNode(builder.Finish());
        using var first = await runtime.SubmitAsync(Plan(store.Snapshot()));
        await first.WaitForCompletionAsync();

        store.SetTransform(node, Matrix3x2.CreateScale(0.5f));
        using var second = await runtime.SubmitAsync(Plan(store.Snapshot(2)));
        await second.WaitForCompletionAsync();

        Assert.Equal(2, StorageBuffers(backend).Count());
    }

    private static Draw2DScene SolidScene(Color color)
    {
        using var builder = new Draw2DSceneBuilder();
        builder.FillRectangle(new(1, 1, 8, 8), Brush.Solid(color));
        return builder.Finish();
    }

    private static GpuRenderGraphPlan Plan(Draw2DScene scene)
    {
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("color", new(16, 16, GpuFormat.Rgba8Unorm));
        graph.AddClearPass("clear", new(target, TextureClearValue.Color(Vector4.Zero)));
        graph.Add2DPass("draw", new(scene, target));
        graph.MarkOutput(target);
        return graph.Compile();
    }

    private static IEnumerable<ManagerTestBackend.Buffer> StorageBuffers(ManagerTestBackend backend)
        => backend.Created.OfType<ManagerTestBackend.Buffer>().Where(b => b.Description.Usage.HasFlag(GpuBufferUsage.Storage));
}
