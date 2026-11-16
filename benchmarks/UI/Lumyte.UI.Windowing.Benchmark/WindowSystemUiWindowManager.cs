using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.UI.Windowing.WindowSystem;
using Lumyte.Windowing;
using Lumyte.Windowing.Abstractions;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Windowing.Benchmark;

/// <summary>Public WindowSystem adapter CPU protocol only; no physical OS windows or GPU performance.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison")]
public class WindowSystemUiWindowManager
{
    /// <summary>Runs the public adapter over an in-process test platform, with real manager request gates.</summary>
    /// <returns>The actual geometry acknowledgement count.</returns>
    public static int RunProtocol()
    {
        using var platform = new ProtocolPlatform();
        using var system = new Lumyte.Windowing.WindowSystem(platform);
        WindowNode node = system.CreateNativeWindow(new() { ClientSize = new(400, 300) }, WindowRole.Normal);
        node.Show();
        node.Activate();
        platform.PumpEvents();
        system.Update();
        var input = new OriginSource();
        var presentations = new Presentations();
        using var windows = new Lumyte.UI.Windowing.WindowSystem.WindowSystemUiWindowManager(system, input, presentations, new(true, true));
        var source = new WindowSystemUiWindowSource(node, input, presentations);
        UiWindowRegistration root = windows.Register(source);
        using var surface = new UiSurface(Button(width: 20, height: 20));
        Task<UiWindowBinding> bindRoot = windows.BindPresentationAsync(root.Window, surface).AsTask();
        void Pump()
        {
            for (int i = 0; i < 25; i++)
            {
                platform.PumpEvents();
                system.Update();
                windows.Advance();
                presentations.Present();
            }
        }

        Pump();
        UiWindowBinding rootBinding = bindRoot.GetAwaiter().GetResult();
        UiWindowId popup = windows.Create(new(root.Window, new(80, 60), UiWindowRole.Popup, UiWindowInputMode.Interactive, new(root.Window, new(0, 0, 20, 20), UiWindowPlacementSide.Below), UiWindowPolicy.RequireNative));
        using var menu = new UiSurface(Button(width: 40, height: 20));
        Task<UiWindowBinding> bindPopup = windows.BindPresentationAsync(popup, menu).AsTask();
        Pump();
        UiWindowBinding popupBinding = bindPopup.GetAwaiter().GetResult();
        UiWindowRequestId shown = windows.Show(popup);
        Pump();
        Require(windows.TryGetRequest(shown, out UiWindowRequestResult? result) && result!.Status == UiWindowRequestStatus.Succeeded);
        Require(windows.TryGetSnapshot(popup, out UiWindowSnapshot? snapshot) && snapshot!.State == UiWindowState.Open && snapshot.Presentation == UiWindowPresentation.Native);
        Require(snapshot!.KeyboardRoot == root.Window && snapshot.InputOrigin != source.CaptureState().InputOrigin);
        Require(platform.Windows.Count == 2 && platform.Windows[1].State.Activation == WindowActivation.PreserveFocus);
        Task popupRetirement = popupBinding.DisposeAsync().AsTask();
        Pump();
        popupRetirement.GetAwaiter().GetResult();
        windows.Close(popup);
        Pump();
        windows.Release(popup);
        Task rootRetirement = rootBinding.DisposeAsync().AsTask();
        Pump();
        rootRetirement.GetAwaiter().GetResult();
        root.Dispose();
        windows.BeginStop();
        Pump();
        Require(root.IsReleased && windows.IsStopped && presentations.Prepared == presentations.Disposed);
        Require(platform.Windows[1].Disposed && presentations.Acknowledged > 0);
        return presentations.Acknowledged;
    }

    /// <summary>Runs fresh root and native popup protocol acquisition, requests and retirement.</summary>
    /// <returns>The actual acknowledged presentation count.</returns>
    [Benchmark]
    public int NativeAdapterProtocolLifecycle() => RunProtocol();

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Native adapter CPU protocol oracle failed.");
        }
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

    private sealed class Presentations : IUiNativePresentationFactory
    {
        private readonly List<Presentation> _live = [];

        internal int Prepared { get; private set; }

        internal int Disposed { get; private set; }

        internal int Acknowledged { get; private set; }

        public Vector2 GetClientLimit(IWindow owner, Rect2D anchor) => new(800, 600);

        public ValueTask<IUiWindowPresentation> PrepareAsync(IWindow window, UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var presentation = new Presentation(this, view);
            _live.Add(presentation);
            Prepared++;
            return ValueTask.FromResult<IUiWindowPresentation>(presentation);
        }

        internal void Present()
        {
            foreach (Presentation presentation in _live)
            {
                if (presentation.PresentedGeometryRevision != presentation.View.GeometryRevision)
                {
                    presentation.PresentedGeometryRevision = presentation.View.GeometryRevision;
                    Acknowledged++;
                }
            }
        }

        private sealed class Presentation(Presentations owner, UiWindowPresentationView view) : IUiWindowPresentation
        {
            public ulong PresentedGeometryRevision { get; set; }

            internal UiWindowPresentationView View { get; private set; } = view;

            public void Update(UiWindowPresentationView next) => View = next;

            public ValueTask DisposeAsync()
            {
                owner._live.Remove(this);
                owner.Disposed++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
