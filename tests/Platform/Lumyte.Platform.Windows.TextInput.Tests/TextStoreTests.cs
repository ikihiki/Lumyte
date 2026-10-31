using System.Runtime.Versioning;
using Lumyte.Platform.Windows.TextInput.Interop;
using Lumyte.TextEditing;
using Xunit;

namespace Lumyte.Platform.Windows.TextInput.Tests;

/// <summary>Defines text store tests.</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed unsafe class TextStoreTests
{
    /// <summary>Read Requires Lock And Returns Bounded Utf16 Buffers.</summary>
    [Fact]
    public void ReadRequiresLockAndReturnsBoundedUtf16Buffers()
    {
        var sink = new Sink();
        var document = new TextDocument("A😀Z");
        var store = new TsfTextStore(document, _ => default, () => 0, sink);
        using var lifetime = new StoreLifetime(store);
        uint copied, runs;
        int next;
        char* text = stackalloc char[2];
        Assert.Equal(TsfTextStore.NoLock, store.GetText(0, -1, text, 2, &copied, null, 0, &runs, &next));
        sink.Lock = _ =>
        {
            char* buffer = stackalloc char[2];
            uint count, runCount;
            int end;
            Assert.Equal(0, store.GetText(0, -1, buffer, 2, &count, null, 0, &runCount, &end));
            Assert.Equal(2u, count);
            Assert.Equal(2, end);
            Assert.Equal('A', buffer[0]);
            return 0;
        };
        int result;
        Assert.Equal(0, store.RequestLock(3, &result));
        Assert.Equal(0, result);
    }

    /// <summary>Nested Synchronous Lock Is Denied And Async Upgrade Waits For Advance.</summary>
    [Fact]
    public void NestedSynchronousLockIsDeniedAndAsyncUpgradeWaitsForAdvance()
    {
        var sink = new Sink();
        var document = new TextDocument("old");
        var store = new TsfTextStore(document, _ => default, () => 0, sink);
        using var lifetime = new StoreLifetime(store);
        int grants = 0;
        sink.Lock = flags =>
        {
            grants++;
            if (grants == 1)
            {
                int nested;
                Assert.Equal(0, store.RequestLock(7, &nested));
                Assert.Equal(TsfTextStore.SynchronousLockDenied, nested);
                Assert.Equal(0, store.RequestLock(6, &nested));
                Assert.Equal(TsfTextStore.AsyncLock, nested);
                Assert.Equal("old", document.CaptureSnapshot().Text);
            }
            else
            {
                TsTextChange change;
                string value = "new";
                fixed (char* text = value)
                {
                    Assert.Equal(0, store.SetText(0, 0, 3, text, 3, &change));
                }

                Assert.Equal("old", document.CaptureSnapshot().Text);
            }

            return 0;
        };
        int result;
        store.RequestLock(3, &result);
        Assert.Equal(1, grants);
        store.Advance();
        Assert.Equal(2, grants);
        Assert.Equal("new", document.CaptureSnapshot().Text);
    }

    /// <summary>Failed Write Lock Rolls Back Transaction And Gate Rejects Writes.</summary>
    [Fact]
    public void FailedWriteLockRollsBackTransactionAndGateRejectsWrites()
    {
        var sink = new Sink();
        var document = new TextDocument("old");
        bool gated = false;
        var store = new TsfTextStore(document, _ => default, () => 0, sink, isReadOnly: () => gated);
        using var lifetime = new StoreLifetime(store);
        sink.Lock = _ =>
        {
            TsTextChange change;
            fixed (char* text = "new")
            {
                Assert.Equal(gated ? TsfTextStore.ReadOnly : 0, store.SetText(0, 0, 3, text, 3, &change));
            }

            return unchecked((int)0x80004005);
        };
        int result;
        store.RequestLock(7, &result);
        Assert.Equal("old", document.CaptureSnapshot().Text);
        gated = true;
        store.RequestLock(7, &result);
        Assert.Equal("old", document.CaptureSnapshot().Text);
    }

    /// <summary>Checks the SDK QUERYONLY and NOQUERY bits do not interchange query and mutation.</summary>
    [Fact]
    public void InsertAtSelectionHonorsQueryOnlyAndNoQueryFlags()
    {
        var sink = new Sink();
        var document = new TextDocument("A");
        var store = new TsfTextStore(document, _ => default, () => 0, sink);
        using var lifetime = new StoreLifetime(store);
        sink.Lock = _ =>
        {
            int start, end;
            fixed (char* text = "B")
            {
                Assert.Equal(0, store.InsertTextAtSelection(2, text, 1, &start, &end, null));
            }

            Assert.Equal(1, start);
            Assert.Equal(2, end);
            Assert.Equal("A", document.CaptureSnapshot().Text);
            return 0;
        };
        int result;
        store.RequestLock(3, &result);
        Assert.Equal(0, result);
        sink.Lock = _ =>
        {
            TsTextChange change;
            fixed (char* text = "B")
            {
                Assert.Equal(0, store.InsertTextAtSelection(1, text, 1, null, null, &change));
            }

            return 0;
        };
        store.RequestLock(7, &result);
        Assert.Equal(0, result);
        Assert.Equal("AB", document.CaptureSnapshot().Text);
    }

    /// <summary>Checks screen coordinate hit testing and stale layout rejection against copied geometry.</summary>
    [Fact]
    public void GeometryUsesPresentationCoordinatesAndRejectsOutdatedRevision()
    {
        var sink = new Sink();
        var document = new TextDocument("A");
        var store = new TsfTextStore(document, rect => new() { left = (int)rect.X + 10, top = (int)rect.Y + 20, right = (int)(rect.X + rect.Width) + 10, bottom = (int)(rect.Y + rect.Height) + 20 }, () => 1, sink);
        using var lifetime = new StoreLifetime(store);
        store.UpdateGeometry(new(0, 1, new(0, 0, 20, 20), new(10, 0, 1, 20), [new(new(0, 1), new(0, 0, 10, 20))], [new(0, TextCaretAffinity.Leading, new(0, 0, 1, 20)), new(1, TextCaretAffinity.Leading, new(10, 0, 1, 20))]));
        sink.Lock = _ =>
        {
            TsfPoint point = new() { X = 15, Y = 25 };
            int position;
            Assert.Equal(0, store.GetACPFromPoint(1, &point, 0, &position));
            Assert.Equal(0, position);
            point.X = 100;
            Assert.Equal(unchecked((int)0x80040207), store.GetACPFromPoint(1, &point, 0, &position));
            Assert.Equal(0, store.GetACPFromPoint(1, &point, 2, &position));
            Assert.Equal(1, position);
            return 0;
        };
        int result;
        store.RequestLock(3, &result);
        Assert.Equal(0, result);
        document.TryReplace(0, new(0, 1), "B", new(1, 1), TextEditOrigin.Application);
        sink.Lock = _ =>
        {
            TsfRect rectangle;
            int clipped;
            Assert.Equal(TsfTextStore.NoLayout, store.GetTextExt(1, 0, 1, &rectangle, &clipped));
            return 0;
        };
        store.RequestLock(3, &result);
        Assert.Equal(0, result);
    }

    /// <summary>Checks application cancellation notifies the native text store after callbacks and before release.</summary>
    [Fact]
    public void CancellationRestoresCheckpointAndDefersNativeNotification()
    {
        var document = new TextDocument("A");
        using (TextDocumentTransaction edit = document.BeginEdit(TextEditOrigin.Platform))
        {
            edit.SetComposition(new(Guid.NewGuid(), new(1, 0), []));
            edit.Replace(new(1, 0), "かな", new(3, 3));
            edit.Commit();
        }

        var sink = new Sink();
        var store = new TsfTextStore(document, _ => default, () => 0, sink);
        using var lifetime = new StoreLifetime(store);
        store.EndDocumentComposition(TextCompositionEnd.Cancel);
        Assert.Equal("A", document.CaptureSnapshot().Text);
        Assert.True(store.HasPendingWork);
        Assert.Equal(0, sink.TextChanges);
        store.Advance();
        Assert.Equal(1, sink.TextChanges);
        Assert.False(store.HasPendingWork);
    }

    /// <summary>Checks CsWin32 native calls reach the managed store, lock sink and shared COM identity.</summary>
    [Fact]
    public void CsWin32VtableDispatchesCallbacksAndSharesIUnknownIdentity()
    {
        var sink = new Sink();
        var store = new TsfTextStore(new("A"), _ => default, () => 0, sink);
        using var lifetime = new StoreLifetime(store);
        using ComPtr<ITextStoreACP> native = TsfCom.Own<ITextStoreACP>(TsfCom.Borrow<ITextStoreACP>(store));
        using ComPtr<ITfTextEditSink> edits = TsfCom.Query<ITfTextEditSink>((nint)native.Pointer);
        using ComPtr<ITfContextOwnerCompositionSink> composition = TsfCom.Query<ITfContextOwnerCompositionSink>((nint)native.Pointer);
        using ComPtr<IUnknown> unknown = TsfCom.Query<IUnknown>((nint)native.Pointer);
        Assert.True(TsfCom.SameIdentity((nint)unknown.Pointer, (nint)edits.Pointer));
        Assert.True(TsfCom.SameIdentity((nint)unknown.Pointer, (nint)composition.Pointer));
        TsStatus status;
        Assert.Equal(0, (int)native.Pointer->GetStatus(&status));
        Assert.Equal(8u, status.dwStaticFlags);
        int grants = 0;
        sink.Lock = _ =>
        {
            grants++;
            int end;
            Assert.Equal(0, (int)native.Pointer->GetEndACP(&end));
            Assert.Equal(1, end);
            return 0;
        };
        HRESULT result;
        Assert.Equal(0, (int)native.Pointer->RequestLock(3, &result));
        Assert.Equal(0, (int)result);
        Assert.Equal(1, grants);
        var unsupported = Guid.NewGuid();
        void* missing;
        Assert.Equal(unchecked((int)0x80004002), (int)unknown.Pointer->QueryInterface(&unsupported, &missing));
        Assert.True(missing == null);
        store.Release();
    }

    /// <summary>Checks candidate UI callbacks use the CsWin32 vtable and native BOOL representation.</summary>
    [Fact]
    public void CsWin32CandidateCallbackCanSuppressSystemUi()
    {
        var sink = new CandidateSink();
        using ComPtr<ITfUIElementSink> native = TsfCom.Own<ITfUIElementSink>(TsfCom.Borrow<ITfUIElementSink>(sink));
        BOOL show = true;
        Assert.Equal(0, (int)native.Pointer->BeginUIElement(42, &show));
        Assert.False(show);
        Assert.Equal(42u, sink.Element);
        Assert.Equal(0, (int)native.Pointer->UpdateUIElement(42));
        Assert.Equal(0, (int)native.Pointer->EndUIElement(42));
        Assert.Equal(3, sink.Calls);
    }

    private sealed class StoreLifetime(TsfTextStore store) : IDisposable
    {
        public void Dispose() => store.Release();
    }

    private sealed class CandidateSink : ITfUIElementSink.Interface
    {
        internal uint Element { get; private set; }

        internal int Calls { get; private set; }

        public HRESULT BeginUIElement(uint element, BOOL* show)
        {
            Element = element;
            *show = false;
            Calls++;
            return (HRESULT)0;
        }

        public HRESULT UpdateUIElement(uint element)
        {
            Calls++;
            return (HRESULT)0;
        }

        public HRESULT EndUIElement(uint element)
        {
            Calls++;
            return (HRESULT)0;
        }
    }

    private sealed class Sink : ITextStoreACPSink.Interface
    {
        /// <summary>Gets or sets the lock.</summary>
        public Func<uint, int> Lock { get; set; } = _ => (HRESULT)0;

        /// <summary>Gets the number of native text change notifications.</summary>
        public int TextChanges { get; private set; }

        /// <summary>Records a native text change notification.</summary>
        /// <param name="flags">The change flags.</param>
        /// <param name="change">The copied change range.</param>
        /// <returns>A successful HRESULT.</returns>
        public HRESULT OnTextChange(TEXT_STORE_TEXT_CHANGE_FLAGS flags, TsTextChange* change)
        {
            TextChanges++;
            return (HRESULT)0;
        }

        /// <summary>On Selection Change.</summary>
        /// <returns>The operation result.</returns>
        public HRESULT OnSelectionChange() => (HRESULT)0;

        /// <summary>On Layout Change.</summary>
        /// <param name = "code">The code.</param>
        /// <param name = "view">The view.</param>
        /// <returns>The operation result.</returns>
        public HRESULT OnLayoutChange(TsLayoutCode code, uint view) => (HRESULT)0;

        /// <summary>On Status Change.</summary>
        /// <param name = "flags">The flags.</param>
        /// <returns>The operation result.</returns>
        public HRESULT OnStatusChange(uint flags) => (HRESULT)0;

        /// <summary>On Attrs Change.</summary>
        /// <param name = "start">The start.</param>
        /// <param name = "end">The end.</param>
        /// <param name = "count">The count.</param>
        /// <param name = "attributes">The attributes.</param>
        /// <returns>The operation result.</returns>
        public HRESULT OnAttrsChange(int start, int end, uint count, Guid* attributes) => (HRESULT)0;

        /// <summary>On Lock Granted.</summary>
        /// <param name = "flags">The flags.</param>
        /// <returns>The operation result.</returns>
        public HRESULT OnLockGranted(TEXT_STORE_LOCK_FLAGS flags) => (HRESULT)Lock((uint)flags);

        /// <summary>On Start Edit Transaction.</summary>
        /// <returns>The operation result.</returns>
        public HRESULT OnStartEditTransaction() => (HRESULT)0;

        /// <summary>On End Edit Transaction.</summary>
        /// <returns>The operation result.</returns>
        public HRESULT OnEndEditTransaction() => (HRESULT)0;
    }
}
