using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.TextEditing;

namespace Lumyte.Platform.Browser.TextInput;

internal sealed class BrowserTextSession : TextInputSessionBase
{
    private readonly JSObject _handle;
    private readonly IDisposable _registration;
    private readonly Action<BrowserTextSession, BrowserTextPacket> _key;
    private readonly Queue<BrowserTextPacket> _pending = new();
    private ulong _sequence;
    private ulong _appliedRevision;

    private Guid _compositionId;

    internal BrowserTextSession(JSObject handle, TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator, ulong generation, IDisposable registration, Action<BrowserTextSession, BrowserTextPacket> key)
        : base(target, document, navigator, generation)
    {
        _handle = handle;
        _registration = registration;
        _key = key;
        TextDocumentSnapshot snapshot = document.CaptureSnapshot();
        _appliedRevision = snapshot.Revision;
        if (!BrowserTextInterop.Begin(handle, Status.SessionId.ToString("N"), generation.ToString(CultureInfo.InvariantCulture), snapshot.Text, snapshot.Selection.Anchor, snapshot.Selection.Active))
        {
            BrowserTextInterop.Release(handle);
            throw new NotSupportedException("The textarea proxy cannot receive focus.");
        }
    }

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    public override bool IsInputIdle => base.IsInputIdle && _pending.Count == 0 && BrowserTextInterop.Quiescent(_handle);

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    protected override bool CanRelease => _pending.Count == 0 && BrowserTextInterop.EndReady(_handle);

    internal void SynchronizeFromDocument()
    {
        if (!Status.IsActive || _pending.Count != 0 || BrowserTextInterop.Pending(_handle) || BrowserTextInterop.Composing(_handle))
        {
            return;
        }

        TextDocumentSnapshot snapshot = Document.CaptureSnapshot();
        if (snapshot.Revision != _appliedRevision)
        {
            BrowserTextInterop.Synchronize(_handle, snapshot.Text, snapshot.Selection.Anchor, snapshot.Selection.Active);
            _appliedRevision = snapshot.Revision;
        }
    }

    /// <summary>On Geometry Changed.</summary>
    /// <param name = "geometry">The geometry.</param>
    protected override void OnGeometryChanged(TextInputGeometry geometry) => BrowserTextInterop.Geometry(_handle, geometry.Caret.X, geometry.Caret.Y, geometry.Caret.Width, geometry.Caret.Height);

    /// <summary>Begin Ending.</summary>
    /// <param name = "behavior">The behavior.</param>
    protected override void BeginEnding(TextCompositionEnd behavior) => BrowserTextInterop.RequestEnd(_handle);

    /// <summary>Release Core.</summary>
    protected override void ReleaseCore()
    {
        BrowserTextInterop.Release(_handle);
        _registration.Dispose();
    }

    /// <summary>Advance Core.</summary>
    protected override void AdvanceCore()
    {
        BrowserTextPacket[] packets = JsonSerializer.Deserialize(BrowserTextInterop.Drain(_handle), BrowserTextJsonContext.Default.BrowserTextPacketArray) ?? [];
        foreach (BrowserTextPacket packet in packets)
        {
            _pending.Enqueue(packet);
        }

        while (_pending.TryDequeue(out BrowserTextPacket? packet))
        {
            if (packet.Session != Status.SessionId.ToString("N") || !ulong.TryParse(packet.Generation, CultureInfo.InvariantCulture, out ulong generation) || generation != Generation)
            {
                continue;
            }

            if (packet.Kind == "overflow")
            {
                _pending.Clear();
                RequestEnd(TextCompositionEnd.Cancel);
                continue;
            }

            if (!ulong.TryParse(packet.Sequence, CultureInfo.InvariantCulture, out ulong sequence) || sequence != checked(_sequence + 1))
            {
                RequestEnd(TextCompositionEnd.Cancel);
                _pending.Clear();
                throw new InvalidDataException("The DOM text acquisition sequence is incomplete.");
            }

            _sequence = sequence;
            if (packet.Kind == "lost")
            {
                RequestEnd(TextCompositionEnd.Cancel);
                continue;
            }

            if (IsEnding)
            {
                continue;
            }

            if (packet.Kind == "key")
            {
                _key(this, packet);

                // Focus changes must run before the next acquired edit is applied.
                break;
            }

            if (Document.CaptureSnapshot().Revision != _appliedRevision)
            {
                RequestEnd(TextCompositionEnd.Cancel);
                throw new InvalidOperationException("The DOM edit was acquired against an outdated document.");
            }

            Apply(packet);
            _appliedRevision = Document.CaptureSnapshot().Revision;
        }
    }

    private void Apply(BrowserTextPacket packet)
    {
        TextDocumentSnapshot snapshot = Document.CaptureSnapshot();
        string value = TextDocument.Normalize(packet.Value);
        using TextDocumentTransaction edit = Document.BeginEdit(TextEditOrigin.Platform);
        if ((packet.Kind == "start" || packet.Composing) && snapshot.Composition is null)
        {
            _compositionId = Guid.NewGuid();
            int start = Math.Min(snapshot.Selection.Anchor, snapshot.Selection.Active);
            int length = Math.Abs(snapshot.Selection.Active - snapshot.Selection.Anchor);
            var span = new TextSpan(start, length);
            edit.SetComposition(new(_compositionId, span, []));
        }

        if (value != snapshot.Text)
        {
            int prefix = 0;
            while (prefix < value.Length && prefix < snapshot.Text.Length && value[prefix] == snapshot.Text[prefix])
            {
                prefix++;
            }

            if (prefix > 0 && prefix < snapshot.Text.Length && char.IsLowSurrogate(snapshot.Text[prefix]))
            {
                prefix--;
            }

            int suffix = 0;
            while (suffix < value.Length - prefix && suffix < snapshot.Text.Length - prefix && value[value.Length - suffix - 1] == snapshot.Text[snapshot.Text.Length - suffix - 1])
            {
                suffix++;
            }

            if (suffix > 0 && suffix < snapshot.Text.Length && char.IsLowSurrogate(snapshot.Text[snapshot.Text.Length - suffix]))
            {
                suffix--;
            }

            edit.Replace(new(prefix, snapshot.Text.Length - prefix - suffix), value.Substring(prefix, value.Length - prefix - suffix), new(packet.Anchor, packet.Active));
        }
        else
        {
            edit.SetSelection(new(packet.Anchor, packet.Active));
        }

        if (packet.Composing)
        {
            var span = new TextSpan(packet.Start, packet.Length);
            edit.SetComposition(new(_compositionId, span, [new(span, TextUnderline.Solid, true)]));
        }
        else if (packet.Kind == "end" || snapshot.Composition is not null)
        {
            edit.EndComposition(TextCompositionEnd.Commit);
            _compositionId = Guid.Empty;
        }

        edit.Commit();
    }
}
