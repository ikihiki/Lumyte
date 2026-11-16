using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.TextEditing;
using Lumyte.UI.Automation;
using Lumyte.UI.TextEditing.Automation;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.TextInput;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.TextEditing.Benchmarks;

/// <summary>CPU protocol control backend, not a physical operating-system IME or renderer.</summary>
public sealed class TextInputFixture : IDisposable
{
    private readonly HostedUiWindowManager _windows = new();
    private readonly UiWindowRegistration _registration;
    private readonly UiWindowBinding _binding;
    private readonly Backend _backend = new();

    /// <summary>Initializes a new instance of the <see cref="TextInputFixture"/> class over a hosted CPU manager.</summary>
    public TextInputFixture()
    {
        Surface = new(new Compose.Definitions.TextBox
        {
            Text = "seed",
            Key = "editor",
            LayoutProvider = new ProtocolLayoutProvider(),
            OnTextChanged = _ => TextEvents++,
            OnCompositionChanged = _ => CompositionEvents++,
        });
        Surface.SetViewport(new(320, 32));
        Surface.UpdateLayout();
        _registration = _windows.Register(new Source());
        _windows.Advance();
        Task<UiWindowBinding> binding = _windows.BindPresentationAsync(_registration.Window, Surface).AsTask();
        _windows.Advance();
        _binding = binding.GetAwaiter().GetResult();
        Connection = new(Surface, _windows, _registration.Window, _backend, new Resolver(), Clipboard);
        Surface.TryFocus(Editor);
        Pump();
        Automation = new(new ProtocolAutomationHost(this), Connection, allowTextInspection: true);
    }

    /// <summary>Gets the public text automation adapter.</summary>
    public UiTextAutomationHost Automation { get; }

    /// <summary>Gets the live surface.</summary>
    public UiSurface Surface { get; }

    /// <summary>Gets the connected editor.</summary>
    public TextBoxElement Editor => (TextBoxElement)Surface.RootElement;

    /// <summary>Gets the protocol connection.</summary>
    public UiTextInputConnection Connection { get; }

    /// <summary>Gets the controlled asynchronous clipboard.</summary>
    public ProtocolClipboard Clipboard { get; } = new();

    /// <summary>Gets the current public session.</summary>
    public TextInputSessionBase Session => _backend.Current!;

    /// <summary>Gets committed notifications in this wave.</summary>
    public int TextEvents { get; private set; }

    /// <summary>Gets composition notifications in this wave.</summary>
    public int CompositionEvents { get; private set; }

    /// <summary>Resets notification counters, not document revision.</summary>
    public void ResetCounters()
    {
        TextEvents = 0;
        CompositionEvents = 0;
    }

    /// <summary>Advances owner-thread protocol, callbacks and layout without desktop or GPU work.</summary>
    public void Pump()
    {
        _windows.Advance();
        _backend.Advance();
        Connection.Advance();
        Surface.UpdateLayout();
        Connection.PublishGeometry();
    }

    /// <summary>Stops and deterministically retires all borrowed owners.</summary>
    public void Dispose()
    {
        Connection.BeginStop();
        Pump();
        if (!Connection.IsStopped)
        {
            throw new InvalidOperationException("Connection did not drain.");
        }

        Connection.Dispose();
        Task retired = _binding.DisposeAsync().AsTask();
        _windows.Advance();
        retired.GetAwaiter().GetResult();
        _registration.Dispose();
        _windows.Advance();
        _windows.BeginStop();
        _windows.Advance();
        _windows.Dispose();
        Surface.Dispose();
        Clipboard.Dispose();
        _backend.Dispose();
    }

    /// <summary>Explicit async clipboard control; never accesses system clipboard.</summary>
    public sealed class ProtocolClipboard : IClipboard
    {
        private TaskCompletionSource<string?>? _pending;

        /// <inheritdoc/>
        public event Action? Changed;

        /// <inheritdoc/>
        public ClipboardFeatures Features => ClipboardFeatures.None;

        /// <inheritdoc/>
        public ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default)
        {
            _pending = new();
            return new(_pending.Task);
        }

        /// <summary>Completes the pending read synchronously for owner-thread observation.</summary>
        /// <param name="text">The protocol result.</param>
        public void Complete(string text)
        {
            _pending!.SetResult(text);
            _pending = null;
        }

        /// <inheritdoc/>
        public ValueTask SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Changed?.Invoke();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public void Dispose() => _pending?.TrySetCanceled();
    }

    private sealed class ProtocolAutomationHost(TextInputFixture fixture) : IUiAutomationHost
    {
        public TimeProvider TimeProvider => TimeProvider.System;

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => ValueTask.FromResult(action(fixture.Surface));

        public ValueTask NextFrameAsync(CancellationToken cancellationToken = default)
        {
            fixture.Pump();
            return ValueTask.CompletedTask;
        }

        public ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Direct Fill boundary does not acquire a host lease.");
    }

    private sealed class Backend : ITextInputService
    {
        public ProtocolSession? Current { get; private set; }

        public TextInputFeatures Features => TextInputFeatures.CommittedText | TextInputFeatures.Composition | TextInputFeatures.Geometry;

        public ITextInputSession Begin(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator) => Current = new(target, document, navigator);

        public void Advance() => Current?.Advance();

        public IReadOnlyList<TextInputKey> DrainKeys() => [];

        public void Dispose()
        {
        }
    }

    private sealed class ProtocolSession(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator)
        : TextInputSessionBase(target, document, navigator, 1)
    {
        protected override void ReleaseCore()
        {
        }
    }

    private sealed class Resolver : IUiTextInputTargetResolver
    {
        public TextInputTarget Resolve(UiWindowId focusWindow, UiWindowId presentationWindow, Guid surfaceId, Guid editorId) => new(new WindowId(surfaceId, 1), new WindowId(surfaceId, 2), surfaceId, editorId);
    }

    private sealed class Source : IUiWindowSource
    {
        public Guid SourceId { get; } = Guid.NewGuid();

        public UiWindowSourceSnapshot CaptureState() => new(1, null, new(320, 32), Vector2.One, true, true, false, SourceId);

        public ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken) => ValueTask.FromResult<IUiWindowPresentation>(new Presentation(view));

        public bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed)
        {
            transformed = position;
            return ReferenceEquals(this, destination);
        }
    }

    private sealed class Presentation(UiWindowPresentationView view) : IUiWindowPresentation
    {
        public ulong PresentedGeometryRevision { get; private set; } = view.GeometryRevision;

        public void Update(UiWindowPresentationView view) => PresentedGeometryRevision = view.GeometryRevision;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ProtocolLayoutProvider : IUiTextLayoutProvider
    {
        public UiTextLayout CreateLayout(TextDocumentSnapshot document, float maximumWidth) => new ProtocolLayout(document);
    }

    private sealed class ProtocolLayout(TextDocumentSnapshot document) : UiTextLayout
    {
        public override ulong DocumentRevision => document.Revision;

        public override Vector2 Size => new(document.Text.Length * 8, 20);

        public override TextLayout2D Drawing => throw new NotSupportedException("Protocol fixture never records glyphs.");

        public override IReadOnlyList<UiTextCaret> CaretStops { get; } = TextDocument.GraphemeBoundaries(document.Text).Select(p => new UiTextCaret(p, TextCaretAffinity.Leading)).ToArray();

        public override Rect2D GetCaretBounds(UiTextCaret caret) => new(caret.Position * 8, 0, 1, 20);

        public override IReadOnlyList<Rect2D> GetRangeBounds(TextSpan range) => range.Length == 0 ? [] : [new(range.Start * 8, 0, range.Length * 8, 20)];

        public override UiTextCaret HitTest(Vector2 position) => CaretStops.MinBy(c => MathF.Abs((c.Position * 8) - position.X));

        public override UiTextCaret MoveCaret(UiTextCaret caret, bool forward) => CaretStops[Math.Clamp(CaretStops.ToList().IndexOf(caret) + (forward ? 1 : -1), 0, CaretStops.Count - 1)];
    }
}
