using System.Runtime.Versioning;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Graphics.Wgpu;
using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.Platform.Windows;
using Lumyte.Platform.Windows.TextInput;
using Lumyte.TextEditing;
using Lumyte.UI.SampleSupport;
using Lumyte.UI.TextEditing.Parley;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.TextInput;
using Lumyte.Windowing.Abstractions;
using static Lumyte.UI.TextEditing.Compose;

namespace Lumyte.UI.TextEditing.Sample;

[SupportedOSPlatform("windows10.0.17763")]
internal static class TextInputHost
{
    internal static void Run(string fontPath, bool customCandidates)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new() { Title = "Lumyte text input", ClientSize = new(640, 240), });
        using IInputSource inputSource = platform.CreateInputSource();
        var source = (IWindowInputSource)inputSource;
        using var input = new InputSystem([inputSource]);
        using var text = new WindowsTextInputService(platform);
        using var engine = new ParleyTextEngine2D();
        using Stream font = File.OpenRead(fontPath);
        string family = engine.AddFont(font);
        var layout = new ParleyUiTextLayoutProvider(engine, new() { FontFamily = family, FontSize = 24, });
        string textValue = string.Empty;
        UiSurface? surface = null;
        Compose.Definitions.TextBox BuildEditor() => TextBox(text: textValue, layoutProvider: layout, onTextChanged: change =>
        {
            textValue = change.Text;
            surface!.Rebuild(BuildEditor());
        });
        Compose.Definitions.TextBox editor = BuildEditor();
        using var ownedSurface = new UiSurface(editor);
        surface = ownedSurface;
        var windows = new HostedUiWindowManager();
        var scope = Guid.NewGuid();
        var drawing = new UiHostedDrawingSource(
 () =>
        {
            WindowState state = window.State;
            return new(1, source.TryGetInputWindowId(window.Id, out InputWindowId origin) ? origin : null, state.ClientSize, state.Scale, state.IsVisible, state.IsActive, state.Lifetime != WindowLifetime.Open, scope);
        });
        window.Show();
        platform.PumpEvents();
        UiWindowRegistration registration = windows.Register(drawing);
        Task<UiWindowBinding> bindingTask = windows.BindPresentationAsync(registration.Window, surface).AsTask();
        void PrepareFrame()
        {
            windows.Advance();
            var canvas = new Canvas2D();
            canvas.Begin();
            drawing.Record(canvas);
            canvas.End();
            drawing.CompleteFrame(false);
        }

        while (!bindingTask.IsCompleted)
        {
            PrepareFrame();
        }

        UiWindowBinding binding = bindingTask.GetAwaiter().GetResult();
        var targets = new UiPlatformTextInputTargetResolver(windows, source, id => id == drawing.SourceId ? window : null);
        using IClipboard clipboard = platform.CreateClipboard();
        var connection = new UiTextInputConnection(surface, windows, registration.Window, text, targets, clipboard);
        var popups = new UiPopupManager(windows);
        using var candidate = new UiImeCandidateController(connection, new(windows, layout), popups, new() { Policy = customCandidates ? ImeCandidateUiPolicy.PreferCustom : ImeCandidateUiPolicy.System, });
        var router = new UiHostInputRouter(source, source, (origin, inputDevice, value) => popups.Dispatch(new(origin, value, inputDevice)));
        using UiPopupOwner popupOwner = popups.RegisterOwner(registration.Window, surface);
        router.Attach(window, surface);
        var pending = new Queue<InputRecord>();
        input.Recorded += pending.Enqueue;
        using IDisposable retained = window.RetainNativeTarget();
        using var device = WgpuDevice.Create();
        var target = (WindowsWindow)window;
        using IGraphicsSurface graphics = device.CreateSurface(Ahjo.Wgpu.SurfaceSource.WindowsHwnd(target.HInstance, target.Hwnd));
        using var presenter = new UiHostPresenter(device);
        IGraphicsSwapchain? swapchain = null;
        void Advance()
        {
            platform.PumpEvents();
            windows.Advance();
            text.Advance();
            input.Update();
            var acquired = new List<(ulong Order, InputRecord? Record, TextInputKey? Key)>();
            while (pending.TryDequeue(out InputRecord record))
            {
                acquired.Add((record.Capture?.Sequence ?? 0, record, null));
            }

            foreach (TextInputKey key in text.DrainKeys())
            {
                acquired.Add((key.Capture.Sequence, null, key));
            }

            foreach ((ulong _, InputRecord? record, TextInputKey? key) in acquired.OrderBy(item => item.Order))
            {
                if (record is not null)
                {
                    router.Dispatch(record.Value);
                }
                else
                {
                    connection.DispatchKey(key!);
                }

                connection.SynchronizeFocus();
            }

            connection.Advance();
            candidate.Advance();
            popups.Advance();
            surface.UpdateLayout();
            connection.PublishGeometry();
            candidate.PublishGeometry();
            var canvas = new Canvas2D();
            canvas.Begin();
            drawing.Record(canvas);
            DrawList2D commands = canvas.End();
            bool visible = UiHostDrawing.Drawable(window.State);
            if (visible)
            {
                SwapchainDesc configuration = UiHostPresenter.Configuration(graphics, window.State.PixelSize.Width, window.State.PixelSize.Height);
                swapchain ??= graphics.CreateSwapchain(configuration);
                if (swapchain.Configuration != configuration)
                {
                    swapchain.Reconfigure(configuration);
                }

                presenter.DrawAsync(swapchain, commands).GetAwaiter().GetResult();
            }

            drawing.CompleteFrame(visible);
        }

        try
        {
            _ = candidate.PrepareAsync();
            while (window.State.Lifetime == WindowLifetime.Open)
            {
                Advance();
            }
        }
        finally
        {
            connection.BeginStop();
            popups.BeginStop();
            while (!connection.IsStopped || !popups.IsStopped)
            {
                Advance();
            }

            candidate.Dispose();
            connection.Dispose();
            popups.Dispose();
            router.Detach(surface);
            Task retired = binding.DisposeAsync().AsTask();
            while (!retired.IsCompleted)
            {
                PrepareFrame();
            }

            retired.GetAwaiter().GetResult();
            registration.Dispose();
            while (!registration.IsReleased)
            {
                PrepareFrame();
            }

            windows.BeginStop();
            while (!windows.IsStopped)
            {
                PrepareFrame();
            }

            windows.Dispose();
            swapchain?.Dispose();
        }
    }
}
