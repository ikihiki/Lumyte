using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Drawing2D.Tests;
using Lumyte.Graphics.RenderGraph;
using Lumyte.UI.SampleSupport;
using Xunit;
using static Lumyte.UI.Compose;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.UI.IntegrationTests;

/// <summary>Checks the UI graph adapter borrows its parent and retains every prepared frame independently.</summary>
public sealed class GraphFrameTests
{
    /// <summary>Checks failed cleanup never returns the renderer before preparation and retries only unfinished steps.</summary>
    /// <param name="failPrepared">Whether cleanup fails at the prepared resources or renderer.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisposalRetriesInDependencyOrder(bool failPrepared)
    {
        var calls = new List<string>();
        using var frame = new UiGraphFrame(new Resource("prepared", calls, failPrepared), new Resource("renderer", calls, !failPrepared));
        Assert.Throws<InvalidOperationException>(frame.Dispose);
        Assert.Equal(failPrepared ? ["prepared"] : new[] { "prepared", "renderer" }, calls);
        frame.Dispose();
        frame.Dispose();
        Assert.Equal(failPrepared ? ["prepared", "prepared", "renderer"] : new[] { "prepared", "renderer", "renderer" }, calls);
    }

    /// <summary>Checks a failed second append returns only its own preparation and leaves the successful frame owned.</summary>
    /// <param name="cancelled">Whether preparation is cancelled or resource declaration fails.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedAppendDoesNotDisposePreviousFrameAsync(bool cancelled)
    {
        using var graph = new Graph(new DispatchDevice());
        using var texture = new DispatchTexture();
        RenderTargetBinding2D target = Target(graph, texture);
        var renderers = new DispatchRenderers();
        using UiGraphFrame first = await UiGraphFrame.AppendAsync(graph, List(), target, new(graph), renderers.CreateTable());
        using var cancel = new CancellationTokenSource();
        var failing = new DispatchRenderers
        {
            OnPreparing = _ =>
            {
                if (cancelled)
                {
                    cancel.Cancel();
                }
            },
            OnAppend = () => throw new InvalidOperationException("Append declaration failed."),
        };
        if (cancelled)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await UiGraphFrame.AppendAsync(graph, List(), target, new(graph), failing.CreateTable(), cancellationToken: cancel.Token));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await UiGraphFrame.AppendAsync(graph, List(), target, new(graph), failing.CreateTable()));
        }

        Assert.All(failing.Batches, batch => Assert.Equal(1, batch.DisposeCount));
        Assert.Equal(0, Assert.Single(renderers.Batches).DisposeCount);
        graph.Dispose();
        first.Dispose();
        Assert.Equal(1, Assert.Single(renderers.Batches).DisposeCount);
        Assert.False(texture.Disposed);
    }

    /// <summary>Checks mismatched graph bindings fail without taking ownership of either graph.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ForeignBindingsFailBeforePreparationAsync()
    {
        using var graph = new Graph(new DispatchDevice());
        using var other = new Graph(new DispatchDevice());
        using var texture = new DispatchTexture();
        var renderers = new DispatchRenderers();
        await Assert.ThrowsAsync<ArgumentException>(async () => await UiGraphFrame.AppendAsync(graph, List(), Target(graph, texture), new(other), renderers.CreateTable()));
        Assert.Empty(renderers.Batches);
        other.AddPass("still borrowed", _ => { }).KeepAlive();
    }

    /// <summary>Checks compile/record failures leave prepared resources owned until commands and plan have been discarded.</summary>
    /// <param name="compileFailure">Whether to fail compilation rather than recording.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GraphFailureRetainsFrameUntilExplicitCleanupAsync(bool compileFailure)
    {
        using var graph = new Graph(new DispatchDevice());
        using var texture = new DispatchTexture();
        var renderers = new DispatchRenderers();
        using UiGraphFrame frame = await UiGraphFrame.AppendAsync(graph, List(), Target(graph, texture), new(graph), renderers.CreateTable());
        if (compileFailure)
        {
            RenderGraphPass first = graph.AddPass("cycle.first", _ => { }).KeepAlive();
            RenderGraphPass second = graph.AddPass("cycle.second", _ => { }).DependsOn(first).KeepAlive();
            first.DependsOn(second);
            Assert.Throws<InvalidOperationException>(() => graph.Compile());
        }
        else
        {
            graph.AddPass("host failure", _ => throw new InvalidOperationException("Record failed.")).KeepAlive();
            using CompiledRenderGraph plan = graph.Compile();
            using var commands = new DispatchCommands();
            Assert.Throws<InvalidOperationException>(() => plan.Record(commands));
        }

        Assert.Equal(0, Assert.Single(renderers.Batches).DisposeCount);
        graph.Dispose();
        frame.Dispose();
        Assert.Equal(1, Assert.Single(renderers.Batches).DisposeCount);
    }

    private static RenderGraphDrawList2D List()
    {
        using var surface = new UiSurface(Panel()[Rectangle(width: 8, height: 8, fill: new Lumyte.Graphics.Drawing2D.Color2D(1, 0, 0, 1))]);
        surface.SetViewport(new(16, 16));
        surface.UpdateLayout();
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        surface.Record(canvas);
        return canvas.End();
    }

    private static RenderTargetBinding2D Target(Graph graph, DispatchTexture texture) => new()
    {
        Texture = graph.ImportTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite)),
        Description = new() { Width = 16, Height = 16, Format = texture.Format, Usage = texture.Usage },
        LoadOp = AttachmentLoadOp.Load,
        PixelScale = 1,
    };

    private sealed class Resource(string name, List<string> calls, bool failOnce) : IDisposable
    {
        private bool _failed;

        public void Dispose()
        {
            calls.Add(name);
            if (failOnce && !_failed)
            {
                _failed = true;
                throw new InvalidOperationException("Retry cleanup.");
            }
        }
    }
}
