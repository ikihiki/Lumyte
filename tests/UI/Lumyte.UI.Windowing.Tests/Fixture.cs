using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Windowing.Tests;

internal sealed class Fixture : IDisposable
{
    private UiPopupManager? _popups;

    private bool _popupDisposed;

    internal Fixture()
    {
        Root = Panel()[Button(width: 20, height: 20, testId: "anchor", onClick: () => Clicks++)];
        Surface = new(Root);
        Anchor = (ButtonElement)Surface.RootElement.Children[0];
        Registration = Windows.Register(Source);
        Task<UiWindowBinding> binding = Windows.BindPresentationAsync(Registration.Window, Surface).AsTask();
        Pump();
        Binding = binding.GetAwaiter().GetResult();
        Owner = Popups.RegisterOwner(Registration.Window, Surface);
    }

    internal TestSource Source { get; } = new();

    internal HostedUiWindowManager Windows { get; } = new();

    internal UiPopupManager Popups => _popups ??= new(Windows);

    internal Definitions.Panel Root { get; }

    internal ButtonElement Anchor { get; }

    internal UiSurface Surface { get; }

    internal UiWindowRegistration Registration { get; }

    internal UiWindowBinding Binding { get; }

    internal UiPopupOwner Owner { get; }

    internal int Clicks { get; set; }

    public void Dispose()
    {
        Popups.BeginStop();
        Pump();
        Assert.True(Popups.IsStopped);
        Popups.Dispose();
        _popupDisposed = true;
        Task retired = Binding.DisposeAsync().AsTask();
        Pump();
        Assert.True(retired.IsCompletedSuccessfully);
        Registration.Dispose();
        Pump();
        Assert.True(Registration.IsReleased);
        Windows.BeginStop();
        Pump();
        Windows.Dispose();
        Surface.Dispose();
    }

    internal void Pump(int frames = 12)
    {
        for (int i = 0; i < frames; i++)
        {
            Windows.Advance();
            if (!_popupDisposed)
            {
                Popups.Advance();
            }

            Source.Present();
        }
    }

    internal UiPopupHandle Open(Action? click = null, UiPopupInputMode mode = UiPopupInputMode.Menu, Action<UiPopupOpened>? opened = null, Action<UiPopupClosed>? closed = null)
    {
        UiPopupHandle popup = Popups.RequestOpen(Owner, new(Anchor, new(80, 60), () => Stack(orientation: UiOrientation.Vertical)[Button(width: 60, height: 20, testId: "item", onClick: click), Button(width: 60, height: 20, testId: "second")], inputMode: mode, onOpened: opened, onClosed: closed));
        Pump();
        return popup;
    }

    internal bool Send(UiInput input) => Popups.Dispatch(new(Source.Origin, input));
}
