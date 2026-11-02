using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Navigation;
using Lumyte.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Application.Navigation.UiPageWindows;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Transitions.SampleSupport;

internal sealed class TransitionScene
{
    internal ManualClock Clock { get; } = new();

    internal UiApplication? Application { get; set; }

    internal UiApplicationWindowContext? Context { get; set; }

    internal IUiNavigator? Navigation { get; set; }

    internal UiNavigationRequest? LastRequest { get; set; }

    internal Signal<bool> PopupOpen { get; } = Signal.Create(false);

    internal int PopupClosed { get; set; }

    internal string PopupCloseCode { get; set; } = string.Empty;

    internal Signal<Color2D> GroupColor { get; } = Signal.Create(new Color2D(0, 0, 1, 1));

    internal Signal<Color2D> PageColor { get; } = Signal.Create(new Color2D(1, 1, 0, 1));

    internal Signal<bool> ShowGroup { get; } = Signal.Create(true);

    internal int Exited { get; set; }

    internal int Entered { get; set; }

    internal int Created { get; set; }

    internal int Disposed { get; set; }

    internal void Configure(UiApplicationBuilder builder, bool sequential = false, bool sameContent = false)
    {
        builder.Configure(new() { AnimationClock = Clock, Motion = new() { MaximumSnapshotPixels = sameContent ? 1 : 8 * 1024 * 1024, MaximumSnapshotBytes = sameContent ? 8 : 64 * 1024 * 1024, PreparationTimeout = TimeSpan.FromSeconds(30), MaximumVisualLifetime = TimeSpan.FromSeconds(180) } });
        builder.Services.AddSingleton(this);
        builder.Services.AddScoped<PageLifetime>();
        UiPageKey<UiNoArgs> details = builder.MapPage<PageLifetime>("/details", (context, lifetime) =>
        {
            _ = lifetime;
            Navigation = context.Activation.Navigation;
            return Stack()[
                Rectangle(width: 80, height: 40, fill: PageColor),
                Stack(orientation: UiOrientation.Horizontal, height: 40)[Rectangle(width: 100, height: 40, fill: new Color2D(1, 1, 1, 1)), Rectangle(width: 80, height: 40, fill: sameContent ? new Color2D(1, 0, 0, 0.5f) : new Color2D(0, 0, 1, 0.5f), sharedElementKey: new UiSharedElementKey("photo"))],
                Button(width: 160, height: 40, testId: "quit", onClick: () => Application!.RequestStop())];
        });
        UiPageKey<UiNoArgs> home = builder.MapPage<PageLifetime>("/", (context, lifetime) =>
        {
            _ = lifetime;
            Navigation = context.Activation.Navigation;
            return Stack()[
                Panel(width: 80, height: 40, testId: "fade", isPresent: ShowGroup, transitions: new UiElementTransitions { Enter = sameContent ? null : new(new() { Opacity = 0 }, new(), Duration.FromSeconds(1), easing: AnimationEasing.Linear), Exit = sameContent ? null : new(new(), new() { Opacity = 0 }, Duration.FromSeconds(1), easing: AnimationEasing.Linear) }, onEntered: () => Entered++, onExited: () => Exited++)[Rectangle(width: 80, height: 40, fill: new Color2D(1, 0, 0, 1)), Rectangle(width: 80, height: 40, fill: GroupColor)],
                Panel(width: 180, height: 40)[Rectangle(width: 40, height: 40, fill: new Color2D(0, 1, 0, 1)), Rectangle(width: 40, height: 40, fill: new Color2D(1, 0, 0, 0.5f), sharedElementKey: new UiSharedElementKey("photo"))],
                Button(key: "anchor-next", width: 160, height: 40, testId: "next", onClick: () => LastRequest = context.Activation.Navigation.RequestNavigate(details, default)),
                Lumyte.UI.Windowing.Compose.Popup(key: "menu", anchor: "anchor-next", isOpen: PopupOpen, presentation: PopupPresentation.Hosted, onDismiss: _ => PopupOpen.Value = false, onClosed: PopupDidClose)[Panel(width: 80, height: 40, transitions: new UiElementTransitions { Exit = new(new(), new() { Opacity = 0 }, Duration.FromSeconds(1), easing: AnimationEasing.Linear) })[Rectangle(width: 80, height: 40, fill: new Color2D(0, 0, 1, 1))]]];
        });
        builder.UseWindows(_ => [Window("main", "Transitions", initialClientSize: new(240, 180), content: services => new Observed(Page(home, options: new() { Transitions = new() { Enter = sequential ? new(new() { Opacity = 0 }, new(), Duration.FromSeconds(1), easing: AnimationEasing.Linear) : null, Exit = sequential ? new(new(), new() { Opacity = 0 }, Duration.FromSeconds(1), easing: AnimationEasing.Linear) : null, Order = sequential ? UiPageTransitionOrder.ExitThenEnter : UiPageTransitionOrder.Together, ConnectSharedElements = true, ConnectedDuration = Duration.FromSeconds(1), ConnectedEasing = AnimationEasing.Linear } })(services), this))]);
    }

    private void PopupDidClose(UiPopupClosed result)
    {
        PopupClosed++;
        PopupCloseCode = $"{result.Reason};{result.Code}";
    }

    private sealed class PageLifetime : IDisposable
    {
        private readonly TransitionScene _scene;

        public PageLifetime(TransitionScene scene)
        {
            _scene = scene;
            scene.Created++;
        }

        public void Dispose() => _scene.Disposed++;
    }

    private sealed class Observed(IUiApplicationContent inner, TransitionScene scene) : IUiApplicationContent, IUiApplicationGraphicsContent
    {
        public bool IsStopped => inner.IsStopped;

        public ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
        {
            scene.Context = context;
            return inner.InitializeAsync(context, cancellationToken);
        }

        public void Advance() => inner.Advance();

        public void BeginStop() => inner.BeginStop();

        public ValueTask RetireAsync() => inner.RetireAsync();

        public ValueTask ReleaseAsync() => inner.ReleaseAsync();

        public ValueTask RebuildGraphicsAsync(UiApplicationWindowContext context) => (inner as IUiApplicationGraphicsContent)?.RebuildGraphicsAsync(context) ?? ValueTask.CompletedTask;
    }
}
