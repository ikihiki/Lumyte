using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.UI.Windowing;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.SampleSupport;

internal sealed class UiPopupExercise : IAsyncDisposable
{
    private readonly UiSurface _surface;

    private readonly Definitions.Widget _anchor;

    private readonly Task<UiWindowBinding> _bindingTask;

    private UiWindowBinding? _binding;

    private UiPopupOwner? _owner;

    private bool _closed;

    internal UiPopupExercise(UiSurface surface, Definitions.Widget anchor, Func<UiWindowSourceSnapshot> capture, string nativeUnavailableCode = "native-unavailable")
    {
        _surface = surface;
        _anchor = anchor;
        Source = new(capture);
        Windows = new(nativeUnavailableCode: nativeUnavailableCode);
        Registration = Windows.Register(Source);
        Popups = new(Windows);
        _bindingTask = Windows.BindPresentationAsync(Registration.Window, surface).AsTask();
    }

    internal UiHostedDrawingSource Source { get; }

    internal HostedUiWindowManager Windows { get; }

    internal UiWindowRegistration Registration { get; }

    internal UiPopupManager Popups { get; }

    internal UiPopupHandle? Current { get; private set; }

    internal int Clicks { get; private set; }

    internal bool Ready => _owner is not null;

    public async ValueTask DisposeAsync()
    {
        Popups.BeginStop();
        while (!Popups.IsStopped)
        {
            Advance();
            await Task.Yield();
        }

        Popups.Dispose();
        _closed = true;
        if (_binding is not null)
        {
            Task retiring = _binding.DisposeAsync().AsTask();
            while (!retiring.IsCompleted)
            {
                Windows.Advance();
                await Task.Yield();
            }

            await retiring;
        }

        Registration.Dispose();
        while (!Registration.IsReleased)
        {
            Windows.Advance();
            await Task.Yield();
        }

        Windows.BeginStop();
        Windows.Advance();
        Windows.Dispose();
    }

    internal void Advance()
    {
        Windows.Advance();
        if (_owner is null && _bindingTask.IsCompletedSuccessfully)
        {
            _binding = _bindingTask.Result;
            _owner = Popups.RegisterOwner(Registration.Window, _surface);
        }

        Popups.Advance();
    }

    internal void Open(bool contextMenu = false)
    {
        if (_closed || _owner is null)
        {
            return;
        }

        UiPopupHandle? handle = null;
        void Save()
        {
            Clicks++;
            Console.WriteLine($"Popup clicks: {Clicks}");
        }

        Definitions.Widget BuildChild() => Button(width: 90, height: 30, testId: "child-save", onClick: Save)[Rectangle(width: 90, height: 30, fill: new Color2D(1, 0, 0, 1))];

        void OpenChild()
        {
            if (handle is not null && Popups.TryGetSurface(handle.Id, out UiSurface? surface))
            {
                Popups.RequestOpen(handle, new(surface!.FocusedWidget!, new(100, 40), BuildChild, side: UiWindowPlacementSide.Right));
            }
        }

        Definitions.Widget BuildMenu() => Stack(orientation: UiOrientation.Vertical)[
            Button(width: 160, height: 32, testId: "popup-save", automationName: "Popup save", onClick: Save)[Rectangle(width: 160, height: 32, fill: new Color2D(0, 1, 0, 1))],
            Button(width: 160, height: 32, testId: "submenu", onClick: OpenChild)[Rectangle(width: 160, height: 32, fill: new Color2D(0.8f, 0.4f, 0, 1))]];

        handle = Popups.RequestOpen(_owner, new(_anchor, new(180, 100), BuildMenu, policy: UiWindowPolicy.PreferNative, role: contextMenu ? UiWindowRole.ContextMenu : UiWindowRole.Popup));
        Current = handle;
    }

    internal bool Dispatch(InputWindowId origin, InputDeviceId? device, UiInput input) => Popups.Dispatch(new(origin, input, device));

    internal async ValueTask<byte[]> CapturePngAsync(IGraphicDevice device, UiWindowId window, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        UiWindowSourceSnapshot state = Source.CaptureState();
        var description = new TextureDesc
        {
            Width = (uint)Math.Ceiling(state.ClientSize.X * state.Scale.X),
            Height = (uint)Math.Ceiling(state.ClientSize.Y * state.Scale.Y),
            Format = TextureFormat.Rgba8Srgb,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        };
        using IGraphicsTexture target = device.CreateTexture(description);
        var canvas = new Canvas2D();
        canvas.Begin();
        byte[] pixels;
        try
        {
            Source.Record(canvas);
            pixels = await UiGraphExercise.DrawAsync(device, target, description, false, canvas.End(), null, null, false, new(0.04f, 0.04f, 0.06f, 1));
            Source.CompleteFrame();
        }
        catch
        {
            Source.CompleteFrame(false);
            throw;
        }

        token.ThrowIfCancellationRequested();
        UiWindowPresentationView view = Source.Presented(window);
        int left = (int)Math.Floor(view.BoundsInPresentation.X * view.Scale.X), top = (int)Math.Floor(view.BoundsInPresentation.Y * view.Scale.Y);
        int width = Math.Min((int)description.Width - left, (int)Math.Ceiling(view.BoundsInPresentation.Width * view.Scale.X));
        int height = Math.Min((int)description.Height - top, (int)Math.Ceiling(view.BoundsInPresentation.Height * view.Scale.Y));
        byte[] crop = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
        {
            pixels.AsSpan(checked((((top + row) * (int)description.Width) + left) * 4), width * 4).CopyTo(crop.AsSpan(row * width * 4));
        }

        return UiSampleScreenshot.Encode((uint)width, (uint)height, crop);
    }

    internal void OutsidePress()
    {
        if (_owner is not null)
        {
            Popups.NotifyOutsidePress(_owner);
        }
    }
}
