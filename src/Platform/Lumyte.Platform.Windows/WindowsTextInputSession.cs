using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumyte.Platform.Abstractions;

namespace Lumyte.Platform.Windows;

/// <summary>Owns one TSF document context and retains its native window.</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsTextInputSession : ITextInputSession
{
    private readonly WindowsTextInputService _service;
    private readonly WindowsPlatform _platform;
    private readonly IDisposable _retention;
    private readonly object _context;
    private readonly TsfTextStore _store;
    private bool _disposed;

    internal WindowsTextInputSession(WindowsTextInputService service, WindowsPlatform platform, ITfThreadMgr manager, uint clientId, WindowsWindow window, ITextInputClient client)
    {
        _service = service;
        _platform = platform;
        Window = window;
        _retention = window.RetainNativeTarget();
        try
        {
            _store = new TsfTextStore(platform, window, client, composing => platform.Notify(() =>
            {
                if (!_disposed)
                {
                    platform.NotifyHandlers(CompositionChanged, composing);
                }
            }));
            manager.CreateDocumentMgr(out ITfDocumentMgr document);
            Document = document;
            try
            {
                document.CreateContext(clientId, 0, _store, out _context, out _);
                document.Push(_context);
            }
            catch
            {
                if (_context is not null)
                {
                    Marshal.ReleaseComObject(_context);
                }

                Marshal.ReleaseComObject(document);
                throw;
            }
        }
        catch
        {
            _retention.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public event Action<bool>? CompositionChanged;

    /// <inheritdoc/>
    public bool IsComposing => _store.IsComposing;

    /// <inheritdoc/>
    public bool UsesSystemCandidateUi => true;

    internal WindowsWindow Window { get; }

    internal ITfDocumentMgr Document { get; }

    /// <inheritdoc/>
    public void Focus()
    {
        Check();
        _service.Focus(this);
    }

    /// <inheritdoc/>
    public void Blur()
    {
        Check();
        _service.Blur(this);
    }

    /// <inheritdoc/>
    public void NotifyTextChanged(int start, int oldLength, int newLength)
    {
        Check();
        _store.NotifyTextChanged(start, oldLength, newLength);
    }

    /// <inheritdoc/>
    public void NotifySelectionChanged()
    {
        Check();
        _store.NotifySelectionChanged();
    }

    /// <inheritdoc/>
    public void NotifyLayoutChanged()
    {
        Check();
        _store.NotifyLayoutChanged();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Check();
        _service.Blur(this);
        Document.Pop(1);
        _store.Detach();
        Marshal.ReleaseComObject(_context);
        Marshal.ReleaseComObject(Document);
        _retention.Dispose();
        _service.Release(this);
        _disposed = true;
    }

    internal void TerminateComposition()
    {
        if (_store.IsComposing)
        {
            ((ITfContextOwnerCompositionServices)_context).TerminateComposition(0);
        }
    }

    internal void InsertCharacter(char character) => _store.InsertCharacter(character);

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _service.Check();
        _store.CheckUnlocked();
    }
}
