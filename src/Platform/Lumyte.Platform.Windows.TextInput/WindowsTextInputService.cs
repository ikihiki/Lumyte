using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.Platform.Windows.TextInput.Interop;
using Lumyte.TextEditing;
using Lumyte.Windowing.Abstractions;
using Windows.Win32;

namespace Lumyte.Platform.Windows.TextInput;

/// <summary>Owns TSF acquisition, document sessions and optional candidate UI on the Windows platform owner.</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed unsafe class WindowsTextInputService : ITextInputService, ITextInputQuiescence, ITextInputInputGate
{
    private readonly WindowsPlatform _platform;
    private readonly ComPtr<ITfThreadMgr> _threadManager;
    private readonly ComPtr<ITfKeystrokeMgr> _keystrokes;
    private readonly IDisposable _filter;
    private readonly WindowsCandidateSource _candidates;
    private readonly Queue<TextInputKey> _keys = new();
    private readonly HashSet<Key> _swallowed = [];
    private readonly HashSet<Key> _blockedAtBegin = [];
    private readonly Dictionary<Key, (WindowsTextSession Session, InputWindowId Owner)> _routed = [];
    private readonly bool _ownsCom;
    private WindowsTextSession? _session;
    private ulong _generation;
    private ulong _keySequence;
    private bool _disposed;
    private int _inputGates;
    private int _writeGates;
    private char? _highSurrogate;
    private uint _clientId;

    /// <summary>Initializes a new instance of the <see cref = "WindowsTextInputService"/> class.</summary>
    /// <param name = "platform">The platform.</param>
    public WindowsTextInputService(WindowsPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        platform.CheckMutation();
        _platform = platform;
        int initialized = TsfNative.CoInitializeEx(0, 2);
        Marshal.ThrowExceptionForHR(initialized);
        _ownsCom = initialized >= 0;
        ComPtr<ITfThreadMgr>? manager = null;
        ComPtr<ITfKeystrokeMgr>? keys = null;
        WindowsCandidateSource? candidates = null;
        try
        {
            Guid clsid = PInvoke.CLSID_TF_ThreadMgr;
            Guid iid = TsfCom.GuidOf<ITfThreadMgr>();
            Marshal.ThrowExceptionForHR(TsfNative.CoCreateInstance(in clsid, 0, 1, in iid, out nint pointer));
            manager = TsfCom.Own<ITfThreadMgr>(pointer);
            uint client;
            Marshal.ThrowExceptionForHR(manager.Pointer->Activate(&client));
            _clientId = client;
            nint borrowed = TsfCom.Borrow(manager);
            try
            {
                keys = TsfCom.Query<ITfKeystrokeMgr>(borrowed);
            }
            finally
            {
                TsfCom.Return(borrowed);
            }

            candidates = new(manager, () => _session);
            _candidates = candidates;
            _threadManager = manager;
            _keystrokes = keys;
            _filter = platform.RegisterTextFilter(Filter);
        }
        catch
        {
            candidates?.Unadvise();
            TsfCom.Release(keys);
            if (manager is not null)
            {
                manager.Pointer->Deactivate();
                TsfCom.Release(manager);
                candidates?.ReleaseAfterDeactivation();
            }

            if (_ownsCom)
            {
                TsfNative.CoUninitialize();
            }

            throw;
        }
    }

    /// <summary>Gets the features.</summary>
    public TextInputFeatures Features => TextInputFeatures.CommittedText | TextInputFeatures.Composition | TextInputFeatures.Geometry;

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    public bool IsCandidateInputIdle => !_disposed && _keys.Count == 0 && _routed.Count == 0 && _swallowed.Count == 0 && _platform.InputSource?.HeldKeys.Count == 0 && (_session?.IsCandidateInputIdle ?? true);

    /// <summary>Gets a value indicating whether ordinary text input is idle.</summary>
    public bool IsInputIdle => !_disposed && _keys.Count == 0 && _routed.Count == 0 && _swallowed.Count == 0 && _platform.InputSource?.HeldKeys.Count == 0 && (_session?.IsInputIdle ?? true);

    /// <summary>Begin.</summary>
    /// <param name = "target">The target.</param>
    /// <param name = "document">The document.</param>
    /// <param name = "navigator">The navigator.</param>
    /// <returns>The operation result.</returns>
    public ITextInputSession Begin(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator)
    {
        Check();
        ArgumentNullException.ThrowIfNull(target);
        if (_session is not null)
        {
            throw new InvalidOperationException("A keyboard scope already has a text session.");
        }

        WindowsWindow focus = Resolve(target.FocusWindow);
        WindowsWindow presentation = Resolve(target.PresentationWindow);
        if (_platform.InputSource is not { } source || !source.TryGetInputWindowId(focus.Id, out _))
        {
            throw new NotSupportedException("An acquisition source must explicitly own the focus Window.");
        }

        source.ReconcilePhysicalKeys();
        _swallowed.UnionWith(source.HeldKeys);
        _blockedAtBegin.UnionWith(source.HeldKeys);
        source.BeginTextScope();
        _session = new(_threadManager, _candidates, _clientId, focus, presentation, target, document, navigator, checked(++_generation), () => _writeGates != 0);
        _keySequence = 0;
        _highSurrogate = null;
        return _session;
    }

    /// <summary>Advance.</summary>
    public void Advance()
    {
        Check();
        if (_platform.InputSource is { } source)
        {
            _swallowed.IntersectWith(source.HeldKeys);
            _blockedAtBegin.IntersectWith(source.HeldKeys);
            foreach (Key key in _routed.Where(pair => !pair.Value.Session.Status.IsActive && !source.HeldKeys.Contains(pair.Key)).Select(pair => pair.Key).ToArray())
            {
                _routed.Remove(key);
            }
        }

        _candidates.Advance();
        _session?.Advance();
        if (_session?.IsReleased == true)
        {
            _session = null;
            _highSurrogate = null;
        }
    }

    /// <summary>Drain Keys.</summary>
    /// <returns>The operation result.</returns>
    public IReadOnlyList<TextInputKey> DrainKeys()
    {
        Check();
        TextInputKey[] keys = _keys.ToArray();
        _keys.Clear();
        return Array.AsReadOnly(keys);
    }

    /// <summary>Excludes physical edits until the owner releases the token.</summary>
    /// <param name="allowComposition">Allows the current composition to update.</param>
    /// <returns>The owner-thread token.</returns>
    public IDisposable AcquireInputGate(bool allowComposition = false)
    {
        Check();
        if (!allowComposition && _session?.Status.IsComposing == true)
        {
            throw new InvalidOperationException("An active composition must become idle before acquiring text input.");
        }

        _inputGates++;
        if (!allowComposition)
        {
            _writeGates++;
        }

        return new InputGate(this, allowComposition);
    }

    /// <summary>Dispose.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Check();
        if (_session is not null)
        {
            throw new InvalidOperationException("End and drain the active text session before disposing TSF.");
        }

        _filter.Dispose();
        _keys.Clear();
        _routed.Clear();
        _swallowed.Clear();
        _candidates.Unadvise();
        TsfCom.Release(_keystrokes);
        Marshal.ThrowExceptionForHR(_threadManager.Pointer->Deactivate());
        _candidates.ReleaseAfterDeactivation();
        TsfCom.Release(_threadManager);
        if (_ownsCom)
        {
            TsfNative.CoUninitialize();
        }

        _disposed = true;
    }

    private static bool Held(int key) => (TsfNative.GetKeyState(key) & 0x8000) != 0;

    private static bool TargetMismatch(WindowsTextSession session, WindowsWindow window) => session.Target.FocusWindow != window.Id;

    private WindowsWindow Resolve(WindowId id) => _platform.Windows.FirstOrDefault(w => w.Id == id && w.State.Lifetime == WindowLifetime.Open) ?? throw new NotSupportedException("The target does not belong to this platform.");

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _platform.Check();
    }

    private WindowsTextInputDisposition Filter(WindowsWindow window, uint message, nuint wParam, nint lParam, InputCaptureStamp capture)
    {
        if (message is 0x100 or 0x101 or 0x104 or 0x105)
        {
            Key key = Lumyte.Platform.Internal.PhysicalKeys.FromPcScan((int)((lParam >> 16) & 0xFF), (lParam & 0x01000000) != 0);
            bool down = message is 0x100 or 0x104;
            bool repeat = down && (lParam & 0x40000000) != 0;
            if (!down && _routed.Remove(key, out (WindowsTextSession Session, InputWindowId Owner) routed))
            {
                QueueKey(routed.Session, window, routed.Owner, capture, key, false, false);
                return WindowsTextInputDisposition.Consumed;
            }

            if (!down && _swallowed.Remove(key))
            {
                _blockedAtBegin.Remove(key);
                return WindowsTextInputDisposition.Consumed;
            }

            if (down && (_blockedAtBegin.Contains(key) || (_swallowed.Contains(key) && (_session?.Status.IsActive != true || _session.Target.FocusWindow != window.Id))))
            {
                return WindowsTextInputDisposition.Consumed;
            }

            if (_inputGates != 0)
            {
                if (down)
                {
                    _swallowed.Add(key);
                }

                return WindowsTextInputDisposition.Consumed;
            }

            WindowsTextSession? session = _session;
            if (session is null || TargetMismatch(session, window))
            {
                return WindowsTextInputDisposition.Pass;
            }

            if (!session.Status.IsActive)
            {
                if (down)
                {
                    _swallowed.Add(key);
                }

                return WindowsTextInputDisposition.Consumed;
            }

            int eaten = 0;
            int result = down ? _keystrokes.Pointer->TestKeyDown((WPARAM)wParam, (LPARAM)lParam, (BOOL*)&eaten) : _keystrokes.Pointer->TestKeyUp((WPARAM)wParam, (LPARAM)lParam, (BOOL*)&eaten);
            Marshal.ThrowExceptionForHR(result);
            if (eaten != 0)
            {
                Marshal.ThrowExceptionForHR(down ? _keystrokes.Pointer->KeyDown((WPARAM)wParam, (LPARAM)lParam, (BOOL*)&eaten) : _keystrokes.Pointer->KeyUp((WPARAM)wParam, (LPARAM)lParam, (BOOL*)&eaten));
            }

            if (eaten != 0)
            {
                if (down)
                {
                    _swallowed.Add(key);
                }

                return WindowsTextInputDisposition.Consumed;
            }

            if (!down)
            {
                return WindowsTextInputDisposition.Consumed;
            }

            bool control = Held(0x11);
            bool shift = Held(0x10);
            TextEditCommand? command = key switch
            {
                Key.ArrowLeft => TextEditCommand.Previous,
                Key.ArrowRight => TextEditCommand.Next,
                Key.Home => TextEditCommand.Start,
                Key.End => TextEditCommand.End,
                Key.Backspace => TextEditCommand.DeleteBackward,
                Key.Delete => TextEditCommand.DeleteForward,
                Key.A when control => TextEditCommand.SelectAll,
                _ => null,
            };
            if (command is { } edit && !session.Status.IsComposing)
            {
                TextEditingCommands.Execute(session.Document, session.Navigator, edit, shift);
                _swallowed.Add(key);
                return WindowsTextInputDisposition.Consumed;
            }

            if (key is Key.Tab or Key.Enter or Key.Escape || (control && key is Key.C or Key.X or Key.V))
            {
                InputWindowId owner = Origin(session.Target.FocusWindow);
                _routed.TryAdd(key, (session, owner));
                QueueKey(session, window, owner, capture, key, true, repeat);
                return WindowsTextInputDisposition.Consumed | WindowsTextInputDisposition.Yield;
            }

            _swallowed.Add(key);
            return WindowsTextInputDisposition.Consumed | WindowsTextInputDisposition.Translate;
        }

        if (message is 0x102 or 0x109 && _session is { } current && current.Target.FocusWindow == window.Id)
        {
            if (_inputGates != 0 || !current.Status.IsActive || wParam < 0x20 || wParam == 0x7F || wParam == 0xFFFF)
            {
                return WindowsTextInputDisposition.Consumed;
            }

            string? text = null;
            if (message == 0x109)
            {
                text = char.ConvertFromUtf32(checked((int)wParam));
            }
            else
            {
                char character = (char)wParam;
                if (char.IsHighSurrogate(character))
                {
                    _highSurrogate = character;
                }
                else if (char.IsLowSurrogate(character))
                {
                    if (_highSurrogate is { } high)
                    {
                        text = new([high, character]);
                        _highSurrogate = null;
                    }
                }
                else
                {
                    _highSurrogate = null;
                    text = character.ToString();
                }
            }

            if (text is not null && current.Document.CaptureSnapshot().Composition is null)
            {
                TextEditingCommands.ReplaceSelection(current.Document, text, TextEditOrigin.Platform);
            }

            return WindowsTextInputDisposition.Consumed;
        }

        return WindowsTextInputDisposition.Pass;
    }

    private InputWindowId Origin(WindowId window) => _platform.InputSource!.TryGetInputWindowId(window, out InputWindowId origin) ? origin : throw new InvalidOperationException("The acquisition origin has expired.");

    private void QueueKey(WindowsTextSession session, WindowsWindow window, InputWindowId owner, InputCaptureStamp capture, Key key, bool down, bool repeat)
    {
        if (_keys.Count >= 4096)
        {
            session.RequestEnd(TextCompositionEnd.Cancel);
            throw new InvalidOperationException("The text key acquisition limit was exceeded.");
        }

        _keys.Enqueue(new(Origin(window.Id), owner, capture, session.Status.SessionId, session.Generation, checked(++_keySequence), key, down, repeat, Held(0x10), Held(0x11), Held(0x12), Held(0x5B) || Held(0x5C)));
    }

    private sealed class InputGate(WindowsTextInputService owner, bool allowComposition) : IDisposable
    {
        private WindowsTextInputService? _owner = owner;

        /// <summary>Dispose.</summary>
        public void Dispose()
        {
            WindowsTextInputService? current = _owner;
            if (current is null)
            {
                return;
            }

            current.Check();
            _owner = null;
            current._inputGates--;
            if (!allowComposition)
            {
                current._writeGates--;
            }
        }
    }
}
