using Lumyte.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.TextServices;

namespace Lumyte.Platform.Windows;

internal sealed unsafe class TsfThread
{
    [ThreadStatic]
    private static TsfThread? s_current;

    private ITfThreadMgr? manager;
    private ITfKeystrokeMgr? keystrokeManager;
    private ITfUIElementMgr? uiElementManager;
    private TsfCandidateSink? candidateSink;
    private TsfDocument? focusedDocument;
    private uint candidateSinkCookie;
    private uint clientId;
    private int referenceCount;

    private TsfThread()
    {
    }

    public static TsfThread? Acquire()
    {
        if (s_current is { } current)
        {
            current.referenceCount++;
            return current;
        }

        var thread = new TsfThread();
        try
        {
            _ = PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED);
            Guid classId = PInvoke.CLSID_TF_ThreadMgr;
            PInvoke.CoCreateInstance(
                in classId,
                null,
                CLSCTX.CLSCTX_INPROC_SERVER,
                out ITfThreadMgr? manager);
            manager!.Activate(out thread.clientId);
            thread.manager = manager;
            try
            {
                thread.keystrokeManager = (ITfKeystrokeMgr)manager;
            }
            catch (InvalidCastException)
            {
                thread.keystrokeManager = null;
            }

            try
            {
                thread.uiElementManager = (ITfUIElementMgr)manager;
                thread.candidateSink = new(
                    thread.uiElementManager,
                    () => thread.focusedDocument?.Store.CurrentClient);
                Guid interfaceId = typeof(ITfUIElementSink).GUID;
                ((ITfSource)manager).AdviseSink(
                    in interfaceId,
                    thread.candidateSink,
                    out thread.candidateSinkCookie);
            }
            catch
            {
                thread.uiElementManager = null;
                thread.candidateSink = null;
                thread.candidateSinkCookie = 0;
            }
        }
        catch
        {
            return null;
        }

        thread.referenceCount = 1;
        s_current = thread;
        return thread;
    }

    public TsfDocument? CreateDocument(
        Func<ITextInputClient?> getClient,
        Func<HWND> getWindow,
        Func<float> getScale)
    {
        if (manager is null)
        {
            return null;
        }

        try
        {
            manager.CreateDocumentMgr(out ITfDocumentMgr? documentManager);
            var store = new TsfTextStore(getClient, getWindow, getScale);
            documentManager!.CreateContext(
                clientId,
                0,
                store,
                out ITfContext? context,
                out _);
            documentManager.Push(context);
            store.AttachContext(context!);
            if (context is ITfSource source)
            {
                Guid interfaceId = typeof(ITfTextEditSink).GUID;
                source.AdviseSink(in interfaceId, store, out _);
            }

            return new(this, documentManager, store);
        }
        catch
        {
            return null;
        }
    }

    public void SetFocus(TsfDocument? document)
    {
        try
        {
            manager?.SetFocus(document?.DocumentManager);
            focusedDocument = document;
        }
        catch
        {
        }
    }

    public void ClearFocus(TsfDocument document)
    {
        if (ReferenceEquals(focusedDocument, document))
        {
            SetFocus(null);
        }
    }

    public bool HandleKeyDown(ushort virtualKey, nint keyData)
    {
        if (keystrokeManager is null)
        {
            return false;
        }

        keystrokeManager.TestKeyDown(new(virtualKey), new(keyData), out BOOL eaten);
        if (!eaten)
        {
            return false;
        }

        keystrokeManager.KeyDown(new(virtualKey), new(keyData), out BOOL consumed);
        return consumed;
    }

    public bool HandleKeyUp(ushort virtualKey, nint keyData)
    {
        if (keystrokeManager is null)
        {
            return false;
        }

        keystrokeManager.TestKeyUp(new(virtualKey), new(keyData), out BOOL eaten);
        if (!eaten)
        {
            return false;
        }

        keystrokeManager.KeyUp(new(virtualKey), new(keyData), out BOOL consumed);
        return consumed;
    }

    public void Release()
    {
        if (--referenceCount > 0)
        {
            return;
        }

        try
        {
            if (candidateSinkCookie != 0 && manager is ITfSource source)
            {
                source.UnadviseSink(candidateSinkCookie);
            }

            manager?.Deactivate();
        }
        catch
        {
        }

        manager = null;
        keystrokeManager = null;
        uiElementManager = null;
        candidateSink = null;
        focusedDocument = null;
        candidateSinkCookie = 0;
        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
    }
}

internal sealed class TsfDocument(
    TsfThread thread,
    ITfDocumentMgr documentManager,
    TsfTextStore store) : IDisposable
{
    internal ITfDocumentMgr? DocumentManager { get; private set; } = documentManager;

    internal TsfTextStore Store { get; } = store;

    public void Focus() => thread.SetFocus(this);

    public void Dispose()
    {
        try
        {
            DocumentManager?.Pop(0);
        }
        catch
        {
        }

        Store.ClearComposition();
        DocumentManager = null;
    }
}
