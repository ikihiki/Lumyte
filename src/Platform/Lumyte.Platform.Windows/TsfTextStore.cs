using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Windows;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class TsfTextStore(WindowsPlatform platform, WindowsWindow window, ITextInputClient client, Action<bool> compositionChanged)
    : ITextStoreACP, ITfContextOwnerCompositionSink
{
    private const int InvalidArgument = unchecked((int)0x80070057);
    private const int Unexpected = unchecked((int)0x8000FFFF);
    private const int NotImplemented = unchecked((int)0x80004001);
    private const int NoInterface = unchecked((int)0x80004002);
    private const int InvalidPosition = unchecked((int)0x80040200);
    private const int NoLock = unchecked((int)0x80040201);
    private const int NoObject = unchecked((int)0x80040202);
    private const int NoLayout = unchecked((int)0x80040206);
    private const int Synchronous = unchecked((int)0x80040208);
    private const int ReadOnly = unchecked((int)0x80040209);
    private const int Async = 0x00040300;
    private ITextStoreACPSink? _sink;
    private object? _sinkIdentity;
    private uint _sinkMask;
    private uint _lock;
    private uint _pendingLock;
    private int _compositions;
    private int _knownLength = client.Text.Length;
    private bool _detached;

    internal bool IsComposing => _compositions > 0;

    private bool HasReadLock => !_detached && (_lock & 2) != 0;

    private bool HasWriteLock => !_detached && (_lock & 6) == 6;

    int ITextStoreACP.AdviseSink(ref Guid interfaceId, object sink, uint mask)
    {
        if (interfaceId != typeof(ITextStoreACPSink).GUID || sink is not ITextStoreACPSink typed)
        {
            return NoInterface;
        }

        if (_sinkIdentity is not null && !SameComObject(_sinkIdentity, sink))
        {
            return unchecked((int)0x80040204);
        }

        _sink = typed;
        _sinkIdentity = sink;
        _sinkMask = mask;
        return 0;
    }

    int ITextStoreACP.UnadviseSink(object sink)
    {
        if (_sinkIdentity is null || !SameComObject(_sinkIdentity, sink))
        {
            return unchecked((int)0x80040204);
        }

        _sink = null;
        _sinkIdentity = null;
        _sinkMask = 0;
        return 0;
    }

    int ITextStoreACP.RequestLock(uint flags, out int sessionResult)
    {
        sessionResult = Unexpected;
        if (_detached || _sink is null || (flags & 2) == 0 || (flags & ~7U) != 0)
        {
            return InvalidArgument;
        }

        if (_lock != 0)
        {
            if ((flags & 1) != 0)
            {
                sessionResult = Synchronous;
            }
            else
            {
                _pendingLock |= flags & 6;
                sessionResult = Async;
            }

            return 0;
        }

        uint mode = flags & 6;
        sessionResult = GrantLock(mode);
        if (_pendingLock != 0)
        {
            uint pending = _pendingLock;
            _pendingLock = 0;
            platform.Enqueue(() =>
            {
                if (!_detached && _sink is not null)
                {
                    ((ITextStoreACP)this).RequestLock(pending, out _);
                }
            });
        }

        return 0;
    }

    int ITextStoreACP.GetStatus(out TsStatus status)
    {
        status = new TsStatus { Dynamic = client.IsReadOnly ? 1U : 0U, Static = 8 };
        return 0;
    }

    int ITextStoreACP.QueryInsert(int start, int end, uint count, out int resultStart, out int resultEnd)
    {
        resultStart = start;
        resultEnd = end;
        return ValidRange(start, end) && count <= int.MaxValue ? 0 : InvalidPosition;
    }

    int ITextStoreACP.GetSelection(uint index, uint count, nint selections, out uint fetched)
    {
        fetched = 0;
        if (!HasReadLock)
        {
            return NoLock;
        }

        if ((index != 0 && index != uint.MaxValue) || count == 0 || selections == 0)
        {
            return InvalidArgument;
        }

        int result = Safe(() =>
        {
            TextSelection selection = client.Selection;
            if (!ValidRange(selection.Start, checked(selection.Start + selection.Length)))
            {
                return InvalidPosition;
            }

            var native = new TsSelection
            {
                Start = selection.Start,
                End = selection.Start + selection.Length,
                ActiveEnd = selection.ActiveAtStart ? 1U : 2U,
            };
            Marshal.StructureToPtr(native, selections, false);
            return 0;
        });
        if (result == 0)
        {
            fetched = 1;
        }

        return result;
    }

    int ITextStoreACP.SetSelection(uint count, nint selections)
    {
        if (!HasWriteLock)
        {
            return NoLock;
        }

        if (count != 1 || selections == 0)
        {
            return InvalidArgument;
        }

        return Safe(() =>
        {
            TsSelection selection = Marshal.PtrToStructure<TsSelection>(selections);
            if (!ValidRange(selection.Start, selection.End) || selection.ActiveEnd > 2)
            {
                return InvalidPosition;
            }

            client.SetSelection(new TextSelection(selection.Start, selection.End - selection.Start, selection.ActiveEnd == 1));
            return 0;
        });
    }

    int ITextStoreACP.GetText(int start, int end, nint text, uint textCapacity, out uint textLength, nint runs, uint runCapacity, out uint runCount, out int next)
    {
        textLength = 0;
        runCount = 0;
        next = start;
        if (!HasReadLock)
        {
            return NoLock;
        }

        string document = client.Text;
        if (end == -1)
        {
            end = document.Length;
        }

        if (!ValidRange(start, end) || textCapacity > int.MaxValue || (textCapacity > 0 && text == 0) || (runCapacity > 0 && runs == 0))
        {
            return InvalidPosition;
        }

        int length = Math.Min(end - start, (int)textCapacity);
        int result = Safe(() =>
        {
            if (length > 0)
            {
                Marshal.Copy(document.ToCharArray(start, length), 0, text, length);
            }

            if (runCapacity > 0 && end > start)
            {
                var run = new TsRunInfo { Count = (uint)(textCapacity == 0 ? end - start : length), Type = 0 };
                Marshal.StructureToPtr(run, runs, false);
            }

            return 0;
        });
        if (result == 0)
        {
            textLength = (uint)length;
            runCount = runCapacity > 0 && end > start ? 1U : 0U;
            next = start + (textCapacity == 0 && runCapacity > 0 ? end - start : length);
        }

        return result;
    }

    int ITextStoreACP.SetText(uint flags, int start, int end, nint text, uint count, out TsTextChange change)
    {
        change = default;
        if (!HasWriteLock)
        {
            return NoLock;
        }

        if (client.IsReadOnly)
        {
            return ReadOnly;
        }

        if (!ValidRange(start, end) || count > int.MaxValue || (count > 0 && text == 0))
        {
            return InvalidPosition;
        }

        int result = Safe(() =>
        {
            string value = count == 0 ? string.Empty : Marshal.PtrToStringUni(text, (int)count)!;
            client.Replace(start, end - start, value);
            _knownLength = client.Text.Length;
            return 0;
        });
        if (result == 0)
        {
            change = new TsTextChange { Start = start, OldEnd = end, NewEnd = checked(start + (int)count) };
        }

        return result;
    }

    int ITextStoreACP.GetFormattedText(int start, int end, out nint data)
    {
        data = 0;
        return NotImplemented;
    }

    int ITextStoreACP.GetEmbedded(int position, ref Guid service, ref Guid interfaceId, out nint value)
    {
        value = 0;
        return NoObject;
    }

    int ITextStoreACP.QueryInsertEmbedded(nint service, nint format, out bool insertable)
    {
        insertable = false;
        return 0;
    }

    int ITextStoreACP.InsertEmbedded(uint flags, int start, int end, nint data, out TsTextChange change)
    {
        change = default;
        return NotImplemented;
    }

    int ITextStoreACP.InsertTextAtSelection(uint flags, nint text, uint count, out int start, out int end, out TsTextChange change)
    {
        start = 0;
        end = 0;
        change = default;
        if (!HasReadLock || ((flags & 2) == 0 && !HasWriteLock))
        {
            return NoLock;
        }

        TextSelection selection = client.Selection;
        start = selection.Start;
        end = checked(start + selection.Length);
        if (!ValidRange(start, end) || count > int.MaxValue || (flags & ~3U) != 0)
        {
            return InvalidPosition;
        }

        if ((flags & 2) != 0)
        {
            return 0;
        }

        int result = ((ITextStoreACP)this).SetText(0, start, end, text, count, out change);
        if (result == 0)
        {
            end = change.NewEnd;
        }

        return result;
    }

    int ITextStoreACP.InsertEmbeddedAtSelection(uint flags, nint data, out int start, out int end, out TsTextChange change)
    {
        start = 0;
        end = 0;
        change = default;
        return NotImplemented;
    }

    int ITextStoreACP.RequestSupportedAttrs(uint flags, uint count, nint attributes) => 0;

    int ITextStoreACP.RequestAttrsAtPosition(int position, uint count, nint attributes, uint flags) => 0;

    int ITextStoreACP.RequestAttrsTransitioningAtPosition(int position, uint count, nint attributes, uint flags) => 0;

    int ITextStoreACP.FindNextAttrTransition(int start, int halt, uint count, nint attributes, uint flags, out int next, out bool found, out int offset)
    {
        next = halt;
        found = false;
        offset = 0;
        return ValidRange(start, halt) ? 0 : InvalidPosition;
    }

    int ITextStoreACP.RetrieveRequestedAttrs(uint count, nint attributes, out uint fetched)
    {
        fetched = 0;
        return 0;
    }

    int ITextStoreACP.GetEndACP(out int end)
    {
        end = client.Text.Length;
        return HasReadLock ? 0 : NoLock;
    }

    int ITextStoreACP.GetActiveView(out uint view)
    {
        view = 0;
        return 0;
    }

    int ITextStoreACP.GetACPFromPoint(uint view, ref NativeMethods.Point point, uint flags, out int position)
    {
        position = 0;
        return NotImplemented;
    }

    int ITextStoreACP.GetTextExt(uint view, int start, int end, out NativeMethods.Rect rectangle, out bool clipped)
    {
        rectangle = default;
        clipped = true;
        if (!HasReadLock)
        {
            return NoLock;
        }

        if (view != 0 || !ValidRange(start, end))
        {
            return InvalidPosition;
        }

        NativeMethods.Rect resultRectangle = default;
        bool resultClipped = true;
        int result = Safe(() =>
        {
            TextInputBounds geometry = client.GetBounds(start, end - start);
            WindowRect b = geometry.Rectangle;
            if (!float.IsFinite(b.X) || !float.IsFinite(b.Y) || !float.IsFinite(b.Width) || !float.IsFinite(b.Height) || b.Width < 0 || b.Height < 0)
            {
                return NoLayout;
            }

            NativeMethods.Point origin = default;
            if (!NativeMethods.ClientToScreen(window.Hwnd, ref origin))
            {
                return NoLayout;
            }

            resultRectangle = new NativeMethods.Rect
            {
                Left = checked(origin.X + (int)Math.Floor(b.X)),
                Top = checked(origin.Y + (int)Math.Floor(b.Y)),
                Right = checked(origin.X + (int)Math.Ceiling(b.X + b.Width)),
                Bottom = checked(origin.Y + (int)Math.Ceiling(b.Y + b.Height)),
            };
            resultClipped = geometry.IsClipped;
            return 0;
        });
        rectangle = resultRectangle;
        clipped = resultClipped;
        return result;
    }

    int ITextStoreACP.GetScreenExt(uint view, out NativeMethods.Rect rectangle)
    {
        rectangle = default;
        if (view != 0)
        {
            return InvalidArgument;
        }

        NativeMethods.Rect resultRectangle = default;
        int result = Safe(() =>
        {
            if (!NativeMethods.GetClientRect(window.Hwnd, out resultRectangle))
            {
                return NoLayout;
            }

            NativeMethods.Point origin = default;
            if (!NativeMethods.ClientToScreen(window.Hwnd, ref origin))
            {
                return NoLayout;
            }

            resultRectangle.Left += origin.X;
            resultRectangle.Right += origin.X;
            resultRectangle.Top += origin.Y;
            resultRectangle.Bottom += origin.Y;
            return 0;
        });
        rectangle = resultRectangle;
        return result;
    }

    int ITextStoreACP.GetWnd(uint view, out nint nativeWindow)
    {
        nativeWindow = 0;
        if (view != 0 || _detached)
        {
            return InvalidArgument;
        }

        nativeWindow = window.Hwnd;
        return 0;
    }

    int ITfContextOwnerCompositionSink.OnStartComposition(nint composition, out bool accepted)
    {
        accepted = !_detached && !client.IsReadOnly;
        if (accepted && _compositions++ == 0)
        {
            compositionChanged(true);
        }

        return 0;
    }

    int ITfContextOwnerCompositionSink.OnUpdateComposition(nint composition, nint range) => 0;

    int ITfContextOwnerCompositionSink.OnEndComposition(nint composition)
    {
        if (_compositions > 0 && --_compositions == 0)
        {
            compositionChanged(false);
        }

        return 0;
    }

    internal void CheckUnlocked()
    {
        if (_lock != 0)
        {
            throw new InvalidOperationException("Application changes are forbidden during a TSF document lock.");
        }
    }

    internal void NotifyTextChanged(int start, int oldLength, int newLength)
    {
        CheckUnlocked();
        int length = client.Text.Length;
        if (start < 0 || oldLength < 0 || newLength < 0 || (long)start + oldLength > _knownLength
            || (long)_knownLength - oldLength + newLength != length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        _knownLength = length;
        var change = new TsTextChange { Start = start, OldEnd = checked(start + oldLength), NewEnd = checked(start + newLength) };
        if ((_sinkMask & 1) != 0)
        {
            _sink?.OnTextChange(0, ref change);
        }
    }

    internal void NotifySelectionChanged()
    {
        CheckUnlocked();
        if ((_sinkMask & 2) != 0)
        {
            _sink?.OnSelectionChange();
        }
    }

    internal void NotifyLayoutChanged()
    {
        CheckUnlocked();
        if ((_sinkMask & 4) != 0)
        {
            _sink?.OnLayoutChange(1, 0);
        }
    }

    internal void InsertCharacter(char character)
    {
        CheckUnlocked();
        if (client.IsReadOnly)
        {
            return;
        }

        TextSelection selection = client.Selection;
        if (!ValidRange(selection.Start, checked(selection.Start + selection.Length)))
        {
            throw new InvalidOperationException("Client selection is invalid.");
        }

        client.Replace(selection.Start, selection.Length, character.ToString());
        NotifyTextChanged(selection.Start, selection.Length, 1);
        NotifySelectionChanged();
    }

    internal void Detach()
    {
        CheckUnlocked();
        _detached = true;
        _sink = null;
        _sinkIdentity = null;
        _pendingLock = 0;
    }

    private static bool SameComObject(object a, object b)
    {
        nint first = Marshal.GetIUnknownForObject(a);
        nint second = Marshal.GetIUnknownForObject(b);
        try
        {
            return first == second;
        }
        finally
        {
            Marshal.Release(first);
            Marshal.Release(second);
        }
    }

    private bool ValidRange(int start, int end) => start >= 0 && end >= start && end <= client.Text.Length;

    private int GrantLock(uint flags)
    {
        _lock = flags;
        try
        {
            return Safe(() => _sink!.OnLockGranted(flags));
        }
        finally
        {
            _lock = 0;
        }
    }

    private int Safe(Func<int> action)
    {
        try
        {
            platform.Check();
            return platform.InNativeCallback(action);
        }
        catch (Exception ex)
        {
            platform.Notify(() => throw new InvalidOperationException("TSF document callback failed.", ex));
            return Marshal.GetHRForException(ex);
        }
    }
}
