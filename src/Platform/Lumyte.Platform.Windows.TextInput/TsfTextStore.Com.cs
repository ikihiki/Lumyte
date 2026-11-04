namespace Lumyte.Platform.Windows.TextInput;

internal sealed unsafe partial class TsfTextStore
{
    HRESULT ITextStoreACP.Interface.AdviseSink(global::System.Guid* riid, IUnknown* punk, uint dwMask) => (HRESULT)AdviseSink(riid, (nint)punk, dwMask);

    HRESULT ITextStoreACP.Interface.UnadviseSink(IUnknown* punk) => (HRESULT)UnadviseSink((nint)punk);

    HRESULT ITextStoreACP.Interface.RequestLock(uint dwLockFlags, HRESULT* phrSession) => (HRESULT)RequestLock(dwLockFlags, (int*)phrSession);

    HRESULT ITextStoreACP.Interface.GetStatus(TS_STATUS* pdcs) => (HRESULT)GetStatus(pdcs);

    HRESULT ITextStoreACP.Interface.QueryInsert(int acpTestStart, int acpTestEnd, uint cch, int* pacpResultStart, int* pacpResultEnd) => (HRESULT)QueryInsert(acpTestStart, acpTestEnd, cch, (int*)pacpResultStart, (int*)pacpResultEnd);

    HRESULT ITextStoreACP.Interface.GetSelection(uint ulIndex, uint ulCount, TS_SELECTION_ACP* pSelection, uint* pcFetched) => (HRESULT)GetSelection(ulIndex, ulCount, pSelection, pcFetched);

    HRESULT ITextStoreACP.Interface.SetSelection(uint ulCount, TS_SELECTION_ACP* pSelection) => (HRESULT)SetSelection(ulCount, pSelection);

    HRESULT ITextStoreACP.Interface.GetText(int acpStart, int acpEnd, PWSTR pchPlain, uint cchPlainReq, uint* pcchPlainRet, TS_RUNINFO* prgRunInfo, uint cRunInfoReq, uint* pcRunInfoRet, int* pacpNext) => (HRESULT)GetText(acpStart, acpEnd, pchPlain.Value, cchPlainReq, pcchPlainRet, prgRunInfo, cRunInfoReq, pcRunInfoRet, (int*)pacpNext);

    HRESULT ITextStoreACP.Interface.SetText(uint dwFlags, int acpStart, int acpEnd, PCWSTR pchText, uint cch, TS_TEXTCHANGE* pChange) => (HRESULT)SetText(dwFlags, acpStart, acpEnd, pchText.Value, cch, pChange);

    HRESULT ITextStoreACP.Interface.GetFormattedText(int acpStart, int acpEnd, IDataObject** ppDataObject) => (HRESULT)GetFormattedText(acpStart, acpEnd, (nint*)ppDataObject);

    HRESULT ITextStoreACP.Interface.GetEmbedded(int acpPos, global::System.Guid* rguidService, global::System.Guid* riid, IUnknown** ppunk) => (HRESULT)GetEmbedded(acpPos, rguidService, riid, (nint*)ppunk);

    HRESULT ITextStoreACP.Interface.QueryInsertEmbedded(global::System.Guid* pguidService, FORMATETC* pFormatEtc, BOOL* pfInsertable) => (HRESULT)QueryInsertEmbedded(pguidService, (nint)pFormatEtc, (int*)pfInsertable);

    HRESULT ITextStoreACP.Interface.InsertEmbedded(uint dwFlags, int acpStart, int acpEnd, IDataObject* pDataObject, TS_TEXTCHANGE* pChange) => (HRESULT)InsertEmbedded(dwFlags, acpStart, acpEnd, (nint)pDataObject, pChange);

    HRESULT ITextStoreACP.Interface.InsertTextAtSelection(uint dwFlags, PCWSTR pchText, uint cch, int* pacpStart, int* pacpEnd, TS_TEXTCHANGE* pChange) => (HRESULT)InsertTextAtSelection(dwFlags, pchText.Value, cch, (int*)pacpStart, (int*)pacpEnd, pChange);

    HRESULT ITextStoreACP.Interface.InsertEmbeddedAtSelection(uint dwFlags, IDataObject* pDataObject, int* pacpStart, int* pacpEnd, TS_TEXTCHANGE* pChange) => (HRESULT)InsertEmbeddedAtSelection(dwFlags, (nint)pDataObject, (int*)pacpStart, (int*)pacpEnd, pChange);

    HRESULT ITextStoreACP.Interface.RequestSupportedAttrs(uint dwFlags, uint cFilterAttrs, global::System.Guid* paFilterAttrs) => (HRESULT)RequestSupportedAttrs(dwFlags, cFilterAttrs, (nint)paFilterAttrs);

    HRESULT ITextStoreACP.Interface.RequestAttrsAtPosition(int acpPos, uint cFilterAttrs, global::System.Guid* paFilterAttrs, uint dwFlags) => (HRESULT)RequestAttrsAtPosition(acpPos, cFilterAttrs, (nint)paFilterAttrs, dwFlags);

    HRESULT ITextStoreACP.Interface.RequestAttrsTransitioningAtPosition(int acpPos, uint cFilterAttrs, global::System.Guid* paFilterAttrs, uint dwFlags) => (HRESULT)RequestAttrsTransitioningAtPosition(acpPos, cFilterAttrs, (nint)paFilterAttrs, dwFlags);

    HRESULT ITextStoreACP.Interface.FindNextAttrTransition(int acpStart, int acpHalt, uint cFilterAttrs, global::System.Guid* paFilterAttrs, uint dwFlags, int* pacpNext, BOOL* pfFound, int* plFoundOffset) => (HRESULT)FindNextAttrTransition(acpStart, acpHalt, cFilterAttrs, (nint)paFilterAttrs, dwFlags, (int*)pacpNext, (int*)pfFound, (int*)plFoundOffset);

    HRESULT ITextStoreACP.Interface.RetrieveRequestedAttrs(uint ulCount, TS_ATTRVAL* paAttrVals, uint* pcFetched) => (HRESULT)RetrieveRequestedAttrs(ulCount, (nint)paAttrVals, pcFetched);

    HRESULT ITextStoreACP.Interface.GetEndACP(int* pacp) => (HRESULT)GetEndACP((int*)pacp);

    HRESULT ITextStoreACP.Interface.GetActiveView(uint* pvcView) => (HRESULT)GetActiveView(pvcView);

    HRESULT ITextStoreACP.Interface.GetACPFromPoint(uint vcView, global::System.Drawing.Point* ptScreen, uint dwFlags, int* pacp) => (HRESULT)GetACPFromPoint(vcView, ptScreen, dwFlags, (int*)pacp);

    HRESULT ITextStoreACP.Interface.GetTextExt(uint vcView, int acpStart, int acpEnd, RECT* prc, BOOL* pfClipped) => (HRESULT)GetTextExt(vcView, acpStart, acpEnd, prc, (int*)pfClipped);

    HRESULT ITextStoreACP.Interface.GetScreenExt(uint vcView, RECT* prc) => (HRESULT)GetScreenExt(vcView, prc);

    HRESULT ITextStoreACP.Interface.GetWnd(uint vcView, HWND* phwnd) => (HRESULT)GetWnd(vcView, (nint*)phwnd);

    HRESULT ITfContextOwnerCompositionSink.Interface.OnStartComposition(ITfCompositionView* pComposition, BOOL* pfOk) => (HRESULT)OnStartComposition((nint)pComposition, (int*)pfOk);

    HRESULT ITfContextOwnerCompositionSink.Interface.OnUpdateComposition(ITfCompositionView* pComposition, ITfRange* pRangeNew) => (HRESULT)OnUpdateComposition((nint)pComposition, (nint)pRangeNew);

    HRESULT ITfContextOwnerCompositionSink.Interface.OnEndComposition(ITfCompositionView* pComposition) => (HRESULT)OnEndComposition((nint)pComposition);

    HRESULT ITfTextEditSink.Interface.OnEndEdit(ITfContext* pic, uint ecReadOnly, ITfEditRecord* pEditRecord) => (HRESULT)OnEndEdit((nint)pic, ecReadOnly, (nint)pEditRecord);
}
