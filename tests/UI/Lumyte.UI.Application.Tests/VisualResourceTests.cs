using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks actual compositor image leases and retiring budget accounting at GPU boundaries.</summary>
public sealed class VisualResourceTests
{
    /// <summary>Checks a failed GPU wait keeps image storage and its budget charged after visual completion.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ImageAndBudgetSurviveOwnerReleaseAndFailedGpuWaitAsync()
    {
        var budget = new UiVisualBudget(new() { MaximumActiveTransitions = 1, MaximumSnapshotPixels = 100, MaximumSnapshotBytes = 800 });
        using IDisposable active = Assert.IsAssignableFrom<IDisposable>(budget.BeginTransition());
        Assert.Null(budget.BeginTransition());
        var texture = new Texture();
        var view = new View(texture);
        var image = new UiVisualSnapshot(texture, view, Assert.IsAssignableFrom<IDisposable>(budget.Reserve(10, 10)));
        var frame = new UiHostPresenter.Frame(Guid.NewGuid()) { Submission = new Submission() };
        frame.Own(image.Retain());
        image.Dispose();
        image.Dispose();
        Assert.False(image.Released.IsCompleted);
        Assert.Null(budget.Reserve(1, 1));
        await Assert.ThrowsAsync<ArithmeticException>(() => frame.DrainAsync().AsTask());
        Assert.Equal(0, texture.Disposals);
        Assert.Throws<InvalidOperationException>(frame.Dispose);
        active.Dispose();
        using IDisposable replacement = Assert.IsAssignableFrom<IDisposable>(budget.BeginTransition());
        Assert.Null(budget.Reserve(1, 1));
        await frame.DrainAsync();
        frame.Dispose();
        await image.Released;
        Assert.Equal(1, view.Disposals);
        Assert.Equal(1, texture.Disposals);
        using IDisposable pixels = Assert.IsAssignableFrom<IDisposable>(budget.Reserve(10, 10));
        Assert.Throws<ObjectDisposedException>(() => image.Retain());
    }

    /// <summary>Checks configured byte limits independently of the pixel count.</summary>
    [Fact]
    public void AggregatePixelAndByteBudgetsIncludeAllEndpoints()
    {
        var budget = new UiVisualBudget(new() { MaximumSnapshotPixels = 100, MaximumSnapshotBytes = 400 });
        using IDisposable first = Assert.IsAssignableFrom<IDisposable>(budget.Reserve(5, 5));
        using IDisposable second = Assert.IsAssignableFrom<IDisposable>(budget.Reserve(5, 5));
        Assert.Null(budget.Reserve(1, 1));
        Assert.Null(budget.Reserve(uint.MaxValue, uint.MaxValue));
        first.Dispose();
        using IDisposable next = Assert.IsAssignableFrom<IDisposable>(budget.Reserve(5, 5));
    }

    /// <summary>Checks an in-flight composite cannot acknowledge a later final ordinary frame.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CompletingAnOlderCompositeDoesNotOpenThePageGateAsync()
    {
        UiApplicationWindowContext? context = null;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().UseRoot(value =>
        {
            context = value;
            return Panel();
        });
        await using UiApplication application = builder.Build();
        Task running = application.RunAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (application.State != UiApplicationState.Running)
        {
            await Task.Delay(1, timeout.Token);
        }

        IUiWindowPresentation presentation = await application.Dispatcher.InvokeAsync(
            () =>
            {
                UiSurface surface = context!.View.CurrentSurface!;
                UiVisualCoordinator coordinator = Assert.IsType<UiVisualCoordinator>(surface.VisualHost);
                Assert.True(application.Windows.TryGetSnapshot(context.Window, out UiWindowSnapshot? window));
                var source = new UiHostedDrawingSource(() => new(1, null, window!.ClientSize, window.Scale, true, true, false, window.KeyboardScopeId));
                var view = new UiWindowPresentationView(window!.Id, window.SourceId, window.Role, window.BoundsInPresentation, window.ClipInPresentation, window.Scale, 1, true);
                ValueTask<IUiWindowPresentation> prepared = source.PreparePresentationAsync(view, surface, default);
                Assert.True(prepared.IsCompletedSuccessfully);
                IUiWindowPresentation token = prepared.Result;
                var flight = new UiPageVisualFlight(application, coordinator, null, null, static () => false);
                coordinator.Page = flight;
                surface.SetPageTransitioning(true);
                flight.Commit();
                var canvas = new Canvas2D();
                canvas.Begin();
                source.Record(canvas);
                _ = canvas.End();
                flight.Advance(false, window);
                Assert.True(flight.IsFinal);
                source.CompleteFrame(true);
                Assert.False(flight.Completion.IsCompleted);
                Assert.True(surface.PageTransitioning);
                canvas.Begin();
                source.Record(canvas);
                _ = canvas.End();
                source.CompleteFrame(true);
                Assert.True(flight.Completion.IsCompletedSuccessfully);
                Assert.False(surface.PageTransitioning);
                return token;
            },
            timeout.Token);
        await presentation.DisposeAsync();
        await application.StopAsync();
        await running.WaitAsync(timeout.Token);
    }

    /// <summary>Checks a transparent isolated layer never inspects or borrows its image.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ZeroOpacityDoesNotInspectOrBorrowTheImageAsync()
    {
        await using UiApplication application = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).Build();
        using var surface = new UiSurface(Panel());
        var coordinator = new UiVisualCoordinator(application, surface, static () => null);
        var budget = new UiVisualBudget(new());
        var texture = new Texture();
        using var image = new UiVisualSnapshot(texture, new View(texture), Assert.IsAssignableFrom<IDisposable>(budget.Reserve(10, 10)));
        var canvas = new Canvas2D();
        canvas.Begin();
        coordinator.Draw(canvas, image, new(0, 0, 10, 10), new() { Opacity = 0 });
        _ = canvas.End();
        Assert.Empty(coordinator.Recorded);
    }

    private sealed class Texture : IGraphicsTexture
    {
        public uint Width => 10;

        public uint Height => 10;

        public uint MipLevels => 1;

        public uint ArrayLayers => 1;

        public TextureFormat Format => TextureFormat.Rgba16Float;

        public TextureUsage Usage => TextureUsage.Sampled | TextureUsage.RenderAttachment;

        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;

        public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width, Height);

        public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => new View(this);
    }

    private sealed class View(IGraphicsTexture texture) : IGraphicsTextureView
    {
        public IGraphicsTexture Texture => texture;

        public TextureViewInfo Info => throw new NotSupportedException();

        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class Submission : IGraphicsSubmission
    {
        private bool _failed;

        public SubmissionStatus Status => SubmissionStatus.Pending;

        public void Dispose()
        {
        }

        public ValueTask WaitAsync(CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                throw new ArithmeticException("pending-gpu");
            }

            return ValueTask.CompletedTask;
        }
    }
}
