using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.Mathematics.Colors;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.Signals;
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
    [Trait("Execution", "Integration")]
    [Fact]
    public async Task HostedEditorsShareOneBackendAndDrainBeforeSwitchingAsync()
    {
        var backend = new Backend();
        var provider = new LayoutProvider();
        UiApplicationWindowContext? main = null;
        UiApplicationWindowContext? child = null;
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
                    return TextBox(text: "first", layoutProvider: provider, width: 120, height: 24, testId: "first");
                });
            UiWindowDeclaration additional = Window(
                "child",
                "Child",
                root: context =>
                {
                    child = context;
                    return TextBox(text: value, layoutProvider: provider, width: 120, height: 24, testId: "second", onTextChanged: change => value = change.Text);
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
            Assert.True(main.View.CurrentSurface!.TryFocus(main.View.CurrentSurface.RootElement));
        });
        await WaitAsync(app, () => backend.Begins == 1);
        Session old = await app.Dispatcher.InvokeAsync(() => backend.Current!);
        await app.Dispatcher.InvokeAsync(() =>
        {
            old.DelayRelease = true;
            child!.Windows.SetKeyboardTarget(child.Window);
            Assert.True(child.View.CurrentSurface!.TryFocus(child.View.CurrentSurface.RootElement));
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

    /// <summary>Checks validation observes a newly mounted editor inside a locally replaced child without a new root declaration.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task EditorValidationTracksPartialElementPublicationAsync()
    {
        var show = Signal.Create(false);
        Lumyte.UI.Compose.Definitions.Panel root = Lumyte.UI.Compose.Panel()[new SignalChildren(show)];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        await using UiApplication app = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => root).Build();
        app.ValidateEditors(surface);
        show.Value = true;
        surface.UpdateLayout();
        Assert.Same(root, surface.Root);
        UiApplicationException error = Assert.Throws<UiApplicationException>(() => app.ValidateEditors(surface));
        Assert.Equal("invalid-editor", error.Code);
        show.Value = false;
        surface.UpdateLayout();
        Assert.Empty(surface.RootElement.Children[0].Children);
        app.ValidateEditors(surface);
    }

    /// <summary>Checks unrelated paint updates skip editor traversal but editing requirements still invalidate the cache.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task EditorValidationUsesServiceRequirementsInsteadOfEveryTreePublicationAsync()
    {
        var color = Signal.Create(new SRgba<float>(1, 0, 0, 1));
        var readOnly = Signal.Create(true);
        var provider = new LayoutProvider();
        using var surface = new UiSurface(Lumyte.UI.Compose.Panel()[
            Lumyte.UI.Compose.Rectangle(fill: color), TextBox(text: "read only", layoutProvider: provider, isReadOnly: readOnly)]);
        await using UiApplication app = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Lumyte.UI.Compose.Panel()).Build();
        app.ValidateEditors(surface);
        ulong serviceRevision = surface.HostValidationRevision;
        ulong treeRevision = surface.TreeRevision;
        color.Value = new(0, 1, 0, 1);
        surface.UpdateTree();
        Assert.True(surface.TreeRevision > treeRevision);
        Assert.Equal(serviceRevision, surface.HostValidationRevision);
        app.ValidateEditors(surface);
        Assert.Equal(serviceRevision, surface.HostValidationRevision);
        readOnly.Value = false;
        surface.UpdateTree();
        Assert.True(surface.HostValidationRevision > serviceRevision);
        Assert.Equal("text-input-unavailable", Assert.Throws<UiApplicationException>(() => app.ValidateEditors(surface)).Code);
        readOnly.Value = true;
        surface.UpdateTree();
        app.ValidateEditors(surface);
        UiElement editor = surface.RootElement.Children[1];
        surface.Rebuild(Lumyte.UI.Compose.Panel()[Lumyte.UI.Compose.Rectangle(), new Lumyte.UI.TextEditing.Compose.Definitions.TextBox { Text = "read only", IsReadOnly = true }]);
        surface.UpdateTree();
        Assert.Same(editor, surface.RootElement.Children[1]);
        Assert.Equal("invalid-editor", Assert.Throws<UiApplicationException>(() => app.ValidateEditors(surface)).Code);
    }

    /// <summary>Automatic dialog text attachment guards nested dialogs and menus until backend retirement.</summary>
    /// <param name="menu">Selects a menu rather than a nested dialog.</param>
    /// <param name="candidates">Includes a visible candidate binding with delayed retirement.</param>
    /// <returns>The real application-loop verification.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DialogEditorRetirementBlocksNestedAdmissionAsync(bool menu, bool candidates)
    {
        var backend = new Backend { CandidatesEnabled = candidates };
        var provider = new LayoutProvider();
        var open = Signal.Create(false);
        int factories = 0;
        int opened = 0;
        UiApplicationWindowContext? main = null;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1), ImeCandidates = new() { Policy = candidates ? ImeCandidateUiPolicy.PreferCustom : ImeCandidateUiPolicy.System } });
        builder.UseDriver(() => new Driver(backend));
        builder.UseTextInput(_ => provider);
        builder.UseWindows(_ => [Window("main", "Main", root: context =>
        {
            main = context;
            Lumyte.UI.Compose.Definitions.Button nestedContent = Lumyte.UI.Compose.Compute(() =>
            {
                factories++;
                return Lumyte.UI.Compose.Button(width: 40, height: 20);
            });
            Lumyte.UI.Compose.Definitions.Widget nested = menu
                ? Lumyte.UI.Compose.Compute(() => Lumyte.UI.Windowing.Compose.Popup(key: "popup", anchor: "editor", isOpen: open.Value, onOpened: _ => opened++)[nestedContent])
                : Lumyte.UI.Compose.Compute(() => Lumyte.UI.Windowing.Compose.Dialog(key: "inner", isOpen: open.Value, title: "Inner", onOpened: _ => opened++)[nestedContent]);
            return Lumyte.UI.Windowing.Compose.Dialog(key: "outer", title: "Outer", isOpen: true, initialFocus: "editor")[
                Lumyte.UI.Compose.Stack()[TextBox(key: "editor", testId: "editor", text: "outer", layoutProvider: provider, width: 120, height: 24), Lumyte.UI.Compose.Button(testId: "activate", width: 40, height: 20), nested]];
        })]);
        UiApplication app = builder.Build();
        Task running = app.RunAsync();
        Session? old = null;
        try
        {
            await WaitAsync(app, () => backend.Begins == 1 && main!.CaptureDialogs().Count == 1
                && (!candidates || app.Windows.CaptureWindows().Any(window => window.Role == UiWindowRole.ImeCandidates && window.IsVisible)));
            await app.Dispatcher.InvokeAsync(() =>
            {
                old = backend.Current!;
                old.DelayRelease = true;
                using (TextDocumentTransaction edit = old.Document.BeginEdit(TextEditOrigin.Platform))
                {
                    edit.SetComposition(new(Guid.NewGuid(), new(0, 1), []));
                    edit.Commit();
                }

                Assert.True(old.Status.IsComposing);
                factories = 0;
                open.Value = true;
            });
            for (int index = 0; index < 8; index++)
            {
                await main!.Automation.NextFrameAsync();
            }

            await app.Dispatcher.InvokeAsync(() =>
            {
                Assert.False(old!.IsReleased);
                Assert.True(old.Status.IsComposing);
                Assert.Equal(menu ? 1 : 0, factories);
                Assert.Equal(0, opened);
                UiDialogOpened outer = Assert.Single(main!.CaptureDialogs());
                if (!menu)
                {
                    UiWindowSnapshot inner = Assert.Single(app.Windows.CaptureWindows(), window => window.Owner == outer.Window && window.Role == UiWindowRole.Normal);
                    Assert.False(inner.ShowIssued);
                    Assert.False(inner.IsVisible);
                }

                old.DelayRelease = false;
            });
            if (candidates)
            {
                await WaitAsync(app, () => backend.CandidateRetirementStarted);
                for (int index = 0; index < 8; index++)
                {
                    await main!.Automation.NextFrameAsync();
                }

                await app.Dispatcher.InvokeAsync(() =>
                {
                    Assert.True(old!.IsReleased);
                    Assert.Equal(menu ? 1 : 0, factories);
                    Assert.Equal(0, opened);
                    UiDialogOpened outer = Assert.Single(main!.CaptureDialogs());
                    if (!menu)
                    {
                        UiWindowSnapshot inner = Assert.Single(app.Windows.CaptureWindows(), window => window.Owner == outer.Window && window.Role == UiWindowRole.Normal);
                        Assert.False(inner.ShowIssued);
                        Assert.False(inner.IsVisible);
                    }

                    backend.CandidateRetirement.TrySetResult();
                });
            }

            await WaitAsync(app, () => opened == 1);
            Assert.True(old!.IsReleased);
            Assert.Equal(1, factories);
            await app.Dispatcher.InvokeAsync(() => open.Value = false);
            await WaitAsync(app, () => main!.CaptureDialogs().Count == 1
                && app.Windows.CaptureWindows().Count(window => window.Role == UiWindowRole.Normal) == 2);
            IUiAutomationHost outerHost = await app.Dispatcher.InvokeAsync(() => main!.GetDialogAutomation(Assert.Single(main.CaptureDialogs()).DialogId));
            var page = new UiAutomationPage(outerHost);
            await page.GetByTestId("activate").ClickAsync();
            await page.GetByTestId("editor").FillAsync("resumed");
            await WaitAsync(app, () => backend.Begins >= 2);
            await app.Dispatcher.InvokeAsync(() =>
            {
                Assert.False(backend.Current!.IsReleased);
                Assert.Equal("resumed", backend.Current.Document.CaptureSnapshot().CommittedText);
            });
        }
        finally
        {
            if (old is not null)
            {
                await app.Dispatcher.InvokeAsync(() =>
                {
                    old.DelayRelease = false;
                    backend.CandidateRetirement.TrySetResult();
                });
            }

            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await running;
            await app.DisposeAsync();
        }

        Assert.Equal(0, backend.Gates);
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

    private sealed class SignalChildren : Lumyte.UI.Compose.Definitions.Panel
    {
        private readonly UiArgument<bool> _show;
        private bool _showValue;

        internal SignalChildren(UiArgument<bool> show)
        {
            _show = show;
            _showValue = show.GetInitialValue();
            show.AttachTo(this, nameof(_showValue), _showValue, static (target, value) => target._showValue = value);
            if (_showValue)
            {
                Children.Initialize(new Lumyte.UI.TextEditing.Compose.Definitions.TextBox());
            }
        }

        protected override bool RebuildsChildren => true;

        protected override Lumyte.UI.Compose.Definitions.Widget RebuildDeclaration(UiWidgetRebuildContext context) => new SignalChildren(_show);
    }

    private sealed class Driver(Backend backend) : UiApplicationDriver
    {
        internal override ITextInputService TextInput => backend;

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override IUiTextInputTargetResolver CreateTextTargets(UiApplication application) => new Targets();

        internal override ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        internal override ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken) => ValueTask.FromResult<UiDriverWindow>(new Window(key, size, backend));

        internal override void Pump() => backend.Advance();

        internal override Task<bool> DrawAsync(UiDriverWindow window, DrawingSnapshot2D scene) => Task.FromResult(true);

        internal override ValueTask CloseWindowAsync(UiDriverWindow window) => ValueTask.CompletedTask;
    }

    private sealed class Window(string key, Vector2 size, Backend backend) : UiDriverWindow(key)
    {
        private readonly Lumyte.Input.Abstractions.InputWindowId _origin = new(Guid.NewGuid());
        private readonly Guid _scope = Guid.NewGuid();
        private bool _visible;
        private IUiWindowSource? _presentation;

        internal override IUiWindowSource PresentationSource => backend.CandidatesEnabled ? _presentation ??= new DelayedSource(Source, backend) : Source;

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override UiWindowSourceSnapshot CaptureState() => new(1, _origin, size, Vector2.One, _visible, _visible, false, _scope);

        internal override void Show() => _visible = true;

        internal override void SetTitle(string title)
        {
        }
    }

    private sealed class DelayedSource(IUiWindowSource source, Backend backend) : IUiWindowSource, IUiWindowPresentationStager
    {
        public Guid SourceId => source.SourceId;

        private IUiWindowSource Inner => source;

        public UiWindowSourceSnapshot CaptureState() => source.CaptureState();

        public IUiStagedWindowPresentation BeginReplacement(IUiWindowPresentation? current, UiWindowPresentationView view, UiSurface candidate) => ((IUiWindowPresentationStager)source).BeginReplacement(current is DelayedPresentation delayed ? delayed.Inner : current, view, candidate);

        public async ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken)
        {
            IUiWindowPresentation presentation = await source.PreparePresentationAsync(view, surface, cancellationToken);
            return new DelayedPresentation(presentation, backend, view.Role == UiWindowRole.ImeCandidates);
        }

        public bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed) => source.TryTransformPoint(destination is DelayedSource delayed ? delayed.Inner : destination, position, out transformed);
    }

    private sealed class DelayedPresentation(IUiWindowPresentation presentation, Backend backend, bool candidate) : IUiWindowPresentation
    {
        public ulong PresentedGeometryRevision => presentation.PresentedGeometryRevision;

        internal IUiWindowPresentation Inner => presentation;

        public void Update(UiWindowPresentationView view) => presentation.Update(view);

        public async ValueTask DisposeAsync()
        {
            if (candidate && backend.CandidatesEnabled)
            {
                backend.CandidateRetirementStarted = true;
                await backend.CandidateRetirement.Task;
            }

            await presentation.DisposeAsync();
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

        internal bool CandidatesEnabled { get; init; }

        internal TaskCompletionSource CandidateRetirement { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool CandidateRetirementStarted { get; set; }

        internal int Begins { get; private set; }

        internal int Gates { get; private set; }

        public void Dispose()
        {
        }

        public ITextInputSession Begin(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator)
        {
            Assert.True(Current is null || Current.IsReleased);
            Begins++;
            return Current = new(target, document, navigator) { CandidatesEnabled = CandidatesEnabled };
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
        public override ImeCandidateSupport CandidateSupport => new(CandidatesEnabled, CandidatesEnabled, false, string.Empty);

        public override ImeCandidateState CandidateState => new(CandidatesEnabled ? new(Status.SessionId, Generation, 1) : null, ImeCandidatePresentationState.Custom, string.Empty);

        internal bool DelayRelease { get; set; }

        internal bool CandidatesEnabled { get; init; }

        protected override bool CanRelease => !DelayRelease;

        public override ImeCandidateSnapshot? CaptureCandidates() => !CandidatesEnabled || IsReleased ? null
            : new(new(Status.SessionId, Generation, 1), 1, Document.CaptureSnapshot().Revision, [new(0, "candidate")], 0, [0], 0, ImeCandidateOperations.None);

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
