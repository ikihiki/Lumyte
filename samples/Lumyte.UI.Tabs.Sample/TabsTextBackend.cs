using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.TextEditing;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.TextInput;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Tabs.Sample;

// An explicitly borrowed deterministic backend, not an OS IME implementation.
internal sealed class TabsTextBackend : ITextInputService, ITextInputQuiescence, ITextInputInputGate, IUiTextInputTargetResolver
{
    private Session? _current;
    private int _gates;

    public TextInputFeatures Features => TextInputFeatures.CommittedText | TextInputFeatures.Composition | TextInputFeatures.Geometry;

    public bool IsInputIdle => _current?.Status.IsComposing != true;

    public bool IsCandidateInputIdle => true;

    internal int Acquisitions { get; private set; }

    internal bool HasPreedit => _current?.Document.CaptureSnapshot().Composition is not null;

    public TextInputTarget Resolve(UiWindowId focus, UiWindowId presentation, Guid surface, Guid editor) => new(new WindowId(focus.ManagerId, focus.Value), new WindowId(presentation.ManagerId, presentation.Value), surface, editor);

    public ITextInputSession Begin(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator) => _current = new(target, document, navigator);

    public void Advance() => _current?.Advance();

    public IReadOnlyList<TextInputKey> DrainKeys() => [];

    public IDisposable AcquireInputGate(bool allowComposition = false)
    {
        _gates++;
        Acquisitions++;
        return new Gate(() => _gates--);
    }

    public void Dispose()
    {
        if (_gates != 0)
        {
            throw new InvalidOperationException("A panel editing lease was leaked.");
        }

        _current?.Dispose();
    }

    internal void BeginPreedit()
    {
        Session session = _current ?? throw new InvalidOperationException("No actual editing session.");
        using TextDocumentTransaction edit = session.Document.BeginEdit(TextEditOrigin.Platform);
        edit.SetComposition(new(Guid.NewGuid(), new(0, 0), []));
        edit.Commit();
    }

    private sealed class Gate(Action release) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                release();
            }
        }
    }

    private sealed class Session(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator) : TextInputSessionBase(target, document, navigator, 1)
    {
        protected override void ReleaseCore()
        {
        }
    }
}
