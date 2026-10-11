using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.Windows;
using Lumyte.Windowing.Abstractions;
using Xunit;

namespace Lumyte.Platform.Tests;

/// <summary>Verifies native Windows resources on an STA thread when Windows is available.</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsNativeTests
{
    /// <summary>Checks HWND ownership and deferred native closure.</summary>
    [WindowsFact]
    public void NativeWindowRetainsHandleUntilSurfaceOwnerReleasesIt() => RunSta(() =>
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowDescriptor());
        WindowsWindow native = Assert.IsType<WindowsWindow>(window);
        Assert.NotEqual(0, native.Hwnd);
        Assert.NotEqual(0, native.HInstance);
        using IDisposable retained = window.RetainNativeTarget();
        var results = new List<WindowRequestResult>();
        window.RequestCompleted += results.Add;
        WindowRequestId close = window.Close();
        platform.PumpEvents();
        Assert.Equal(WindowLifetime.Closing, window.State.Lifetime);
        Assert.NotEqual(0, native.Hwnd);
        Assert.Empty(results);
        Assert.Throws<InvalidOperationException>(window.Dispose);
        retained.Dispose();
        platform.PumpEvents();
        Assert.Equal(WindowLifetime.Closed, window.State.Lifetime);
        Assert.Equal(close, Assert.Single(results).Request);
        Assert.Equal(WindowRequestStatus.Applied, results[0].Status);
        Assert.Throws<InvalidOperationException>(() => native.Hwnd);
    });

    /// <summary>Checks native owners cannot close before native children.</summary>
    [WindowsFact]
    public void OwnerCloseIsDeniedWhileChildIsAlive() => RunSta(() =>
    {
        using var platform = new WindowsPlatform();
        using IWindow owner = platform.CreateWindow(new WindowDescriptor());
        using IWindow child = platform.CreateWindow(new WindowDescriptor
        {
            Owner = owner,
            Kind = WindowKind.Popup,
            Activation = WindowActivation.PreserveFocus,
        });
        var results = new List<WindowRequestResult>();
        owner.RequestCompleted += results.Add;
        owner.Close();
        platform.PumpEvents();
        Assert.Equal(WindowRequestStatus.Denied, Assert.Single(results).Status);
        Assert.Equal(WindowLifetime.Open, owner.State.Lifetime);
        child.Close();
        platform.PumpEvents();
        owner.Close();
        platform.PumpEvents();
        Assert.Equal(WindowLifetime.Closed, owner.State.Lifetime);
    });

    /// <summary>Checks TSF creates a document context and retains its target.</summary>
    [WindowsFact]
    public void TsfSessionRetainsWindowAndUsesSystemCandidateUi() => RunSta(() =>
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowDescriptor());
        using ITextInputService text = platform.CreateTextInputService();
        using ITextInputSession session = text.CreateSession(window, new TextClient());
        Assert.True(session.UsesSystemCandidateUi);
        Assert.False(session.IsComposing);
        Assert.Throws<InvalidOperationException>(window.Dispose);
        session.Focus();
        session.NotifyLayoutChanged();
        session.Blur();
        session.Dispose();
        window.Dispose();
    });

    /// <summary>Checks TSF lock escalation, UTF-16 edits and application-change notifications.</summary>
    [WindowsFact]
    public void TsfTextStoreHonorsDocumentLocksAndDoesNotDuplicateNativeEdits() => RunSta(() =>
    {
        using var platform = new WindowsPlatform();
        using var window = (WindowsWindow)platform.CreateWindow(new WindowDescriptor());
        var client = new TextClient();
        var store = new TsfTextStore(platform, window, client, _ => { });
        ITextStoreACP native = store;
        int locks = 0;
        int syncResult = 0;
        int asyncResult = 0;
        var sink = new Sink(flags =>
        {
            locks++;
            if (locks == 1)
            {
                Assert.Equal(2U, flags);
                Assert.Equal(0, native.RequestLock(3, out syncResult));
                Assert.Equal(0, native.RequestLock(6, out asyncResult));
                Assert.Equal(unchecked((int)0x80040201), native.SetSelection(0, 0));
            }
            else
            {
                Assert.Equal(6U, flags);
                nint text = Marshal.StringToCoTaskMemUni("😀");
                try
                {
                    Assert.Equal(0, native.SetText(0, 0, 3, text, 2, out TsTextChange change));
                    Assert.Equal(3, change.OldEnd);
                    Assert.Equal(2, change.NewEnd);
                    Assert.Equal("😀", client.Text);
                    Assert.Equal(new TextSelection(2, 0), client.Selection);
                    Assert.Throws<InvalidOperationException>(window.Dispose);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(text);
                }
            }

            return 0;
        });
        Guid id = typeof(ITextStoreACPSink).GUID;
        Assert.Equal(0, native.AdviseSink(ref id, sink, 31));
        Assert.Equal(unchecked((int)0x80040201), native.GetEndACP(out _));
        Assert.Equal(0, native.RequestLock(3, out int result));
        Assert.Equal(0, result);
        Assert.Equal(unchecked((int)0x80040208), syncResult);
        Assert.Equal(0x00040300, asyncResult);
        Assert.Equal(1, locks);
        platform.PumpEvents();
        Assert.Equal(2, locks);
        Assert.Equal(0, sink.TextChanges);
        client.Replace(0, 2, "x");
        store.NotifyTextChanged(0, 2, 1);
        Assert.Equal(1, sink.TextChanges);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.NotifyTextChanged(3, 1, 1));
        store.Detach();
    });

    [SupportedOSPlatform("windows10.0.17763")]
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20)))
        {
            throw new TimeoutException("Native STA test did not finish.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class TextClient : ITextInputClient
    {
        public string Text { get; private set; } = "日本語";

        public TextSelection Selection { get; private set; } = new(3, 0);

        public bool IsReadOnly => false;

        public void Replace(int start, int length, string text)
        {
            Text = Text.Remove(start, length).Insert(start, text);
            Selection = new TextSelection(start + text.Length, 0);
        }

        public void SetSelection(TextSelection selection) => Selection = selection;

        public TextInputBounds GetBounds(int start, int length) => new(new WindowRect(0, 0, 1, 20), false);
    }

    private sealed class Sink(Func<uint, int> grant) : ITextStoreACPSink
    {
        internal int TextChanges { get; private set; }

        public void OnTextChange(uint flags, ref TsTextChange change) => TextChanges++;

        public void OnSelectionChange()
        {
        }

        public void OnLayoutChange(uint code, uint view)
        {
        }

        public void OnStatusChange(uint flags)
        {
        }

        public void OnAttrsChange(int start, int end, uint count, nint attributes)
        {
        }

        public int OnLockGranted(uint flags) => grant(flags);

        public void OnStartEditTransaction()
        {
        }

        public void OnEndEditTransaction()
        {
        }
    }
}
