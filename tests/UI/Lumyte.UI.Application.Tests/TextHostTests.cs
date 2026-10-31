using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.TextEditing;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.TextEditing;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.TextInput;
using Lumyte.Windowing.Abstractions;
using Xunit;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.TextEditing.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Exercises automatic connection ownership using a deterministic backend, without claiming physical IME support.</summary>
public sealed class TextHostTests
{
    /// <summary>Checks cached validation is invalidated when an initially empty descendant acquires an editor.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task EditorValidationSeesLateChildInitializationAsync()
    {
        Lumyte.UI.Compose.Definitions.Panel child = Lumyte.UI.Compose.Panel();
        Lumyte.UI.Compose.Definitions.Panel root = Lumyte.UI.Compose.Panel()[child];
        await using UiApplication app = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => root).Build();
        app.ValidateEditors(root);
        child.Children.Initialize(new Lumyte.UI.TextEditing.Compose.Definitions.TextBox());
        Assert.Throws<UiApplicationException>(() => app.ValidateEditors(root));
    }

    /// <summary>Checks a hosted focus switch drains the old session before beginning another and connects controlled string updates.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task HostedEditorsShareOneBackendAndDrainBeforeSwitchingAsync()
    {
        var backend = new Backend();
        var provider = new LayoutProvider();
        UiApplicationWindowContext? main = null;
        UiApplicationWindowContext? child = null;
        Definitions.TextBox? first = null;
        Definitions.TextBox? second = null;
        string value = "initial";
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.UseDriver(() => new Driver(backend));
        builder.UseTextInput(_ => provider);
        builder.UseWindows(_ =>
        {
            UiWindowDeclaration primary = Window(
                "main",
                "Main",
                root: context =>
                {
                    main = context;
                    return first = TextBox(text: "first", layoutProvider: provider, width: 120, height: 24, testId: "first");
                });
            UiWindowDeclaration additional = Window(
                "child",
                "Child",
                root: context =>
                {
                    child = context;
                    return second = TextBox(text: value, layoutProvider: provider, width: 120, height: 24, testId: "second", onTextChanged: change => value = change.Text);
                },
                initialClientSize: new(220, 100));
            return [primary, additional];
        });
        UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        await app.Dispatcher.InvokeAsync(() =>
        {
            main!.Windows.SetKeyboardTarget(main.Window);
            Assert.True(main.View.CurrentSurface!.TryFocus(first));
        });
        await WaitAsync(app, () => backend.Begins == 1);
        Session old = await app.Dispatcher.InvokeAsync(() => backend.Current!);
        await app.Dispatcher.InvokeAsync(() =>
        {
            old.DelayRelease = true;
            child!.Windows.SetKeyboardTarget(child.Window);
            Assert.True(child.View.CurrentSurface!.TryFocus(second));
        });
        await child!.Automation.NextFrameAsync();
        await child.Automation.NextFrameAsync();
        await app.Dispatcher.InvokeAsync(() =>
        {
            Assert.Equal(1, backend.Begins);
            Assert.False(old.IsReleased);
            old.DelayRelease = false;
        });
        await WaitAsync(app, () => backend.Begins == 2);
        Assert.True(old.IsReleased);
        await new UiAutomationPage(child.Automation).GetByTestId("second").FillAsync("changed");
        Assert.Equal("changed", value);
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
