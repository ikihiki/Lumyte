using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.UI.Windowing.WindowSystem;
using Lumyte.Windowing;
using Lumyte.Windowing.Abstractions;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Windowing.Tests.TestTasks;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Verifies NativeTests.</summary>
public sealed class NativeTests
{
    /// <summary>Checks NativeAdapterRequiresExplicitObservationAndPreservesFocus.</summary>
    /// <param name="nativeAllowed">Whether native input observation is verified.</param>
    /// <param name="failPreparation">Whether native drawing preparation fails once.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeAdapterRequiresExplicitObservationAndPreservesFocus(bool nativeAllowed, bool failPreparation)
    {
        using var platform = new NativeTestPlatform();
        using var system = new Lumyte.Windowing.WindowSystem(platform);
        WindowNode node = system.CreateNativeWindow(new() { ClientSize = new(200, 150), }, WindowRole.Normal);
        node.Show();
        node.Activate();
        system.Update();
        platform.PumpEvents();
        system.Update();
        var input = new OriginSource();
        var renderer = new Renderer
        {
            FailNative = failPreparation,
        };
        var native = new WindowSystemUiWindowManager(system, input, renderer, new(nativeAllowed, nativeAllowed));
        var hosted = new HostedUiWindowManager();
        var windows = new FallbackUiWindowManager(native, hosted);
        var source = new WindowSystemUiWindowSource(node, input, renderer);
        UiWindowRegistration registration = windows.Register(source);
        using var surface = new UiSurface(Button(width: 20, height: 20));
        Task<UiWindowBinding> binding = windows.BindPresentationAsync(registration.Window, surface).AsTask();
        void Pump()
        {
            for (int i = 0; i < 25; i++)
            {
                platform.PumpEvents();
                system.Update();
                windows.Advance();
                renderer.Present();
            }
        }

        Pump();
        UiWindowBinding root = Completed(binding);
        UiWindowId id = windows.Create(new(registration.Window, new(80, 60), UiWindowRole.Popup, UiWindowInputMode.Interactive, new(registration.Window, new(0, 0, 20, 20), UiWindowPlacementSide.Below), UiWindowPolicy.PreferNative));
        using var content = new UiSurface(Button());
        Task<UiWindowBinding> pending = windows.BindPresentationAsync(id, content).AsTask();
        windows.Place(id, new(registration.Window, new(0, 0, 20, 20), UiWindowPlacementSide.Below));
        Pump();
        Assert.True(pending.IsCompletedSuccessfully);
        UiWindowBinding drawing = Completed(pending);
        windows.Show(id);
        Pump();
        windows.TryGetSnapshot(id, out UiWindowSnapshot? snapshot);
        Assert.Equal(UiWindowState.Open, snapshot!.State);
        Assert.Equal(nativeAllowed && !failPreparation ? UiWindowPresentation.Native : UiWindowPresentation.Hosted, snapshot.Presentation);
        Assert.Equal(registration.Window, snapshot.KeyboardRoot);
        if (nativeAllowed && !failPreparation)
        {
            Assert.Equal(WindowActivation.PreserveFocus, platform.Windows[1].State.Activation);
            Assert.False(platform.Windows[1].State.IsActive);
            Assert.NotEqual(snapshot.InputOrigin, source.CaptureState().InputOrigin);
        }

        if (nativeAllowed && !failPreparation)
        {
            UiWindowId child = windows.Create(new(id, new(30, 20), UiWindowRole.Popup, UiWindowInputMode.Interactive, new(id, new(10, 10, 20, 20), UiWindowPlacementSide.Right), UiWindowPolicy.HostedOnly));
            using var submenu = new UiSurface(Button());
            Task<UiWindowBinding> submenuBinding = windows.BindPresentationAsync(child, submenu).AsTask();
            Pump();
            Assert.True(submenuBinding.IsCompletedSuccessfully);
            windows.Show(child);
            Pump();
            windows.TryGetSnapshot(child, out UiWindowSnapshot? childState);
            Assert.Equal(id, childState!.PresentationRoot);
            Assert.Equal(registration.Window, childState.KeyboardRoot);
            windows.Close(child);
            Pump();
            windows.Release(child);
        }

        Assert.All(renderer.Views, view => Assert.Equal(windows.Id, view.Window.ManagerId));
        if (failPreparation)
        {
            Assert.True(renderer.CleanBeforeFallback);
        }

        drawing.DisposeAsync();
        Pump();
        windows.Close(id);
        Pump();
        windows.Release(id);
        root.DisposeAsync();
        Pump();
        registration.Dispose();
        Pump();
        Assert.True(registration.IsReleased);
        windows.BeginStop();
        Pump();
        windows.Dispose();
        native.BeginStop();
        hosted.BeginStop();
        native.Advance();
        hosted.Advance();
        native.Dispose();
        hosted.Dispose();
    }

    private sealed class OriginSource : IWindowInputSource
    {
        private readonly Dictionary<WindowId, InputWindowId> _ids = [];

        public Guid StreamId { get; } = Guid.NewGuid();

        public bool TryGetInputWindowId(WindowId windowId, out InputWindowId id)
        {
            if (!_ids.TryGetValue(windowId, out id))
            {
                id = new(Guid.NewGuid());
                _ids.Add(windowId, id);
            }

            return true;
        }

        public InputEventBatch? PeekEvents() => null;

        public void CommitEvents(ulong batchId)
        {
        }

        public void AbortEvents()
        {
        }

        public void Initialize(IInputDeviceRegistry registry)
        {
        }

        public void Update()
        {
        }

        public void Shutdown()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class Renderer : IUiNativePresentationFactory
    {
        private readonly List<Presentation> _tokens = [];

        private IWindow? _failed;

        internal IEnumerable<UiWindowPresentationView> Views => _tokens.Select(p => p.View);

        internal bool FailNative { get; set; }

        internal bool CleanBeforeFallback { get; private set; }

        public Vector2 GetClientLimit(IWindow owner, Rect2D anchor) => new(800, 600);

        public ValueTask<IUiWindowPresentation> PrepareAsync(IWindow window, UiWindowPresentationView view, UiSurface surface, CancellationToken token)
        {
            if (window.State.Kind == WindowKind.Popup && FailNative)
            {
                FailNative = false;
                _failed = window;
                return ValueTask.FromException<IUiWindowPresentation>(new UiWindowOperationException("presentation-failed"));
            }

            if (_failed is not null && view.Role == UiWindowRole.Popup && window.State.Kind == WindowKind.Normal)
            {
                CleanBeforeFallback = _failed.State.Lifetime == WindowLifetime.Closed;
            }

            var p = new Presentation(view);
            _tokens.Add(p);
            return ValueTask.FromResult<IUiWindowPresentation>(p);
        }

        internal void Present()
        {
            foreach (Presentation token in _tokens)
            {
                token.PresentedGeometryRevision = token.View.GeometryRevision;
            }
        }

        private sealed class Presentation(UiWindowPresentationView view) : IUiWindowPresentation
        {
            public ulong PresentedGeometryRevision { get; set; }

            internal UiWindowPresentationView View { get; private set; } = view;

            public void Update(UiWindowPresentationView next) => View = next;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
