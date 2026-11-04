using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.TextEditing;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.TextEditing;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.TextInput;
using Lumyte.Windowing.Abstractions;
using Xunit;
using static Lumyte.UI.TextEditing.Compose;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Checks navigation quiesces the shared text backend before publishing.</summary>
public sealed class TextHostTests
{
    /// <summary>Checks page publication waits for an old text session and starts the new editor only after explicit focus.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task NavigationDrainsThePreviousEditorBeforePublishingAsync()
    {
        var backend = new Backend();
        var provider = new LayoutProvider();
        var pages = new EditorPages(provider);
        var content = new UiNavigationApplicationContent(pages, navigation => navigation.RequestNavigate("/first"));
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.UseDriver(() => new Driver(backend));
        builder.UseTextInput(_ => provider);
        builder.UseContent(_ => content);
        UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running && content.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        await app.Dispatcher.InvokeAsync(() => Assert.True(content.WindowContext.View.CurrentSurface!.TryFocus(content.WindowContext.View.CurrentSurface.RootElement)));
        await WaitAsync(app, () => backend.Begins == 1);
        Session previous = await app.Dispatcher.InvokeAsync(() => backend.Current!);
        await app.Dispatcher.InvokeAsync(() => previous.DelayRelease = true);
        UiNavigationRequest request = await app.Dispatcher.InvokeAsync(() => content.Navigation.RequestNavigate("/second"));
        await WaitAsync(app, () => content.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Quiescing);
        await app.Dispatcher.InvokeAsync(() =>
        {
            Assert.False(request.Completion.IsCompleted);
            Assert.Equal("/first", content.WindowContext.View.CaptureNavigationSnapshot().Current!.Route);
            Assert.Equal(1, backend.Begins);
            previous.DelayRelease = false;
        });
        Assert.Equal(UiNavigationStatus.Committed, (await request.Completion.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        await WaitAsync(app, () => content.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        Assert.True(previous.IsReleased);
        Assert.Equal(1, backend.Begins);
        await app.Dispatcher.InvokeAsync(() => Assert.True(content.WindowContext.View.CurrentSurface!.TryFocus(content.WindowContext.View.CurrentSurface.RootElement)));
        await WaitAsync(app, () => backend.Begins == 2);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await running;
        Assert.Equal(0, backend.Gates);
        Assert.True(backend.Current!.IsReleased);
        await app.DisposeAsync();
    }

    private static async Task WaitAsync(UiApplication app, Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (app.State == UiApplicationState.Starting)
        {
            await Task.Delay(1, timeout.Token);
        }

        while (true)
        {
            Assert.Null(app.Failure);
            if (await app.Dispatcher.InvokeAsync(condition, timeout.Token))
            {
                break;
            }

            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class EditorPages(LayoutProvider provider) : IUiPageActivationProvider
    {
        public UiPageDestination Capture<TArgs>(UiPageKey<TArgs> key, TArgs arguments) => key.Value is "/first" or "/second" && typeof(TArgs) == typeof(UiNoArgs) ? UiPageDestination.Capture(key, arguments, static value => value) : throw new ArgumentException("Unknown route.");

        public IUiPageActivation Create(UiPageActivationContext context) => new Editor(context, provider);

        private sealed class Editor(UiPageActivationContext context, LayoutProvider provider) : IUiPageActivation
        {
            private Definitions.TextBox? _root;

            public bool IsPrepared => _root is not null;

            public Lumyte.UI.Compose.Definitions.Widget Root => _root ?? throw new InvalidOperationException();

            public IUiPageLeaveGuard? LeaveGuard => null;

            public async ValueTask PrepareAsync(CancellationToken cancellationToken) => _root = await context.Dispatcher.InvokeAsync(() => TextBox(text: context.Destination.Route, layoutProvider: provider), cancellationToken);

            public ValueTask RetireAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Driver(Backend backend) : UiApplicationDriver
    {
        internal override ITextInputService TextInput => backend;

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override IUiTextInputTargetResolver CreateTextTargets(UiApplication application) => new Targets();

        internal override ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        internal override ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken) => ValueTask.FromResult<UiDriverWindow>(new Window(key, size));

        internal override void Pump() => backend.Advance();

        internal override Task<bool> DrawAsync(UiDriverWindow window, DrawList2D commands) => Task.FromResult(true);

        internal override ValueTask CloseWindowAsync(UiDriverWindow window) => ValueTask.CompletedTask;
    }

    private sealed class Window(string key, Vector2 size) : UiDriverWindow(key)
    {
        private readonly Lumyte.Input.Abstractions.InputWindowId _origin = new(Guid.NewGuid());
        private readonly Guid _scope = Guid.NewGuid();
        private bool _visible;

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override UiWindowSourceSnapshot CaptureState() => new(1, _origin, size, Vector2.One, _visible, _visible, false, _scope);

        internal override void Show() => _visible = true;

        internal override void SetTitle(string title)
        {
        }
    }

    private sealed class Targets : IUiTextInputTargetResolver
    {
        public TextInputTarget Resolve(UiWindowId focus, UiWindowId presentation, Guid surface, Guid editor) => new(new WindowId(Guid.NewGuid(), 1), new WindowId(Guid.NewGuid(), 2), surface, editor);
    }

    private sealed class Backend : ITextInputService, ITextInputQuiescence, ITextInputInputGate
    {
        public TextInputFeatures Features => TextInputFeatures.CommittedText | TextInputFeatures.Composition | TextInputFeatures.Geometry;

        public bool IsInputIdle => Current?.Status.IsComposing != true;

        public bool IsCandidateInputIdle => true;

        internal Session? Current { get; private set; }

        internal int Begins { get; private set; }

        internal int Gates { get; private set; }

        public void Dispose()
        {
        }

        public ITextInputSession Begin(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator)
        {
            Assert.True(Current is null || Current.IsReleased);
            Begins++;
            return Current = new(target, document, navigator);
        }

        public void Advance() => Current?.Advance();

        public IReadOnlyList<TextInputKey> DrainKeys() => [];

        public IDisposable AcquireInputGate(bool allowComposition = false)
        {
            Gates++;
            return new Guard(() => Gates--);
        }
    }

    private sealed class Session(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator) : TextInputSessionBase(target, document, navigator, 1)
    {
        internal bool DelayRelease { get; set; }

        protected override bool CanRelease => !DelayRelease;

        protected override void ReleaseCore()
        {
        }
    }

    private sealed class Guard(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    private sealed class LayoutProvider : IUiTextLayoutProvider
    {
        public UiTextLayout CreateLayout(TextDocumentSnapshot document, float maximumWidth) => new Layout(document);
    }

    private sealed class Layout(TextDocumentSnapshot document) : UiTextLayout
    {
        public override ulong DocumentRevision => document.Revision;

        public override Vector2 Size => new(document.CommittedText.Length * 8, 20);

        public override TextLayout2D Drawing { get; } = TextLayout2D.FromDrawings(document.CommittedText, default, default, 1, []);

        public override IReadOnlyList<UiTextCaret> CaretStops => Enumerable.Range(0, document.CommittedText.Length + 1).Select(index => new UiTextCaret(index, TextCaretAffinity.Leading)).ToArray();

        public override Rect2D GetCaretBounds(UiTextCaret caret) => new(caret.Position * 8, 0, 1, 20);

        public override IReadOnlyList<Rect2D> GetRangeBounds(TextSpan range) => [new(range.Start * 8, 0, range.Length * 8, 20)];

        public override UiTextCaret HitTest(Vector2 position) => new(Math.Clamp((int)(position.X / 8), 0, document.CommittedText.Length), TextCaretAffinity.Leading);

        public override UiTextCaret MoveCaret(UiTextCaret caret, bool forward) => new(Math.Clamp(caret.Position + (forward ? 1 : -1), 0, document.CommittedText.Length), caret.Affinity);
    }
}
