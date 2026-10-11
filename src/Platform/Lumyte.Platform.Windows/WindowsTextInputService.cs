using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Windows;

/// <summary>Connects editable UTF-16 documents to Windows Text Services Framework.</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsTextInputService : ITextInputService
{
    private readonly WindowsPlatform _platform;
    private readonly ITfThreadMgr _manager;
    private readonly ITfKeystrokeMgr _keys;
    private readonly Dictionary<WindowsWindow, WindowsTextInputSession> _sessions = [];
    private readonly uint _client;
    private WindowsTextInputSession? _focused;
    private bool _disposed;

    internal WindowsTextInputService(WindowsPlatform platform)
    {
        _platform = platform;
        Type type = Type.GetTypeFromCLSID(new Guid("529A9E6B-6587-4F23-AB9E-9C7D683E3C50"), true)!;
        _manager = (ITfThreadMgr)Activator.CreateInstance(type)!;
        try
        {
            _keys = (ITfKeystrokeMgr)_manager;
            _manager.Activate(out _client);
        }
        catch
        {
            Marshal.ReleaseComObject(_manager);
            throw;
        }
    }

    /// <inheritdoc/>
    public ITextInputSession CreateSession(IWindow window, ITextInputClient client)
    {
        Check();
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(client);
        if (window is not WindowsWindow native || native.Platform != _platform || native.State.Lifetime != WindowLifetime.Open || _sessions.ContainsKey(native))
        {
            throw new ArgumentException("Provide an open window on this platform with no existing text session.", nameof(window));
        }

        var session = new WindowsTextInputSession(this, _platform, _manager, _client, native, client);
        _sessions.Add(native, session);
        return session;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Check();
        if (_sessions.Count != 0)
        {
            throw new InvalidOperationException("Dispose text sessions before the text service.");
        }

        _manager.Deactivate();
        Marshal.ReleaseComObject(_manager);
        _disposed = true;
        _platform.Release(this);
    }

    internal void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _platform.CheckMutation();
    }

    internal void Focus(WindowsTextInputSession session)
    {
        Check();
        if (_focused != session)
        {
            _focused?.TerminateComposition();
        }

        _manager.AssociateFocus(session.Window.Hwnd, session.Document, out ITfDocumentMgr? previous);
        if (previous is not null)
        {
            Marshal.ReleaseComObject(previous);
        }

        _focused = session;
        if (session.Window.State.IsActive)
        {
            _manager.SetFocus(session.Document);
        }
    }

    internal void Blur(WindowsTextInputSession session)
    {
        Check();
        if (session.Window.State.Lifetime != WindowLifetime.Closed)
        {
            session.TerminateComposition();
            _manager.AssociateFocus(session.Window.Hwnd, null, out ITfDocumentMgr? previous);
            if (previous is not null)
            {
                Marshal.ReleaseComObject(previous);
            }
        }

        if (_focused == session)
        {
            _manager.SetFocus(null);
            _focused = null;
        }
    }

    internal void Release(WindowsTextInputSession session) => _sessions.Remove(session.Window);

    internal bool ProcessKey(NativeMethods.Message message)
    {
        if (_focused is null || _focused.Window.State.Lifetime == WindowLifetime.Closed || message.Window != _focused.Window.Hwnd || !_focused.Window.State.IsActive)
        {
            return false;
        }

        bool down = message.Id is 0x100 or 0x104;
        if (!down && message.Id is not (0x101 or 0x105))
        {
            return false;
        }

        bool eaten;
        if (down)
        {
            _keys.TestKeyDown(message.WParam, message.LParam, out eaten);
            if (eaten)
            {
                _keys.KeyDown(message.WParam, message.LParam, out eaten);
            }
        }
        else
        {
            _keys.TestKeyUp(message.WParam, message.LParam, out eaten);
            if (eaten)
            {
                _keys.KeyUp(message.WParam, message.LParam, out eaten);
            }
        }

        return eaten;
    }

    internal void WindowMessage(WindowsWindow window, uint message, nuint wParam)
    {
        if (!_sessions.TryGetValue(window, out WindowsTextInputSession? session))
        {
            return;
        }

        if (message == 0x7 && _focused == session)
        {
            _manager.SetFocus(session.Document);
        }
        else if (message == 0x8 && _focused == session)
        {
            session.TerminateComposition();
            _manager.SetFocus(null);
        }
        else if (message == 0x102 && _focused == session && wParam is >= 0x20 and <= 0xFFFF)
        {
            // Keys eaten by TSF are never translated into WM_CHAR by the pump.
            session.InsertCharacter((char)wParam);
        }
    }
}
