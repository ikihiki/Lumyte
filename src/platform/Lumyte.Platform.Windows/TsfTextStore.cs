using System.Drawing;
using System.Runtime.InteropServices;

using Lumyte.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.TextServices;

namespace Lumyte.Platform.Windows;

internal sealed unsafe class TsfTextStore :
    ITextStoreACP,
    ITfContextOwnerCompositionSink,
    ITfTextEditSink
{
    private const int NotImplemented = unchecked((int)0x80004001);
    private const int NoLayout = unchecked((int)0x80040206);

    private readonly Func<ITextInputClient?> getClient;
    private readonly Func<HWND> getWindow;
    private readonly Func<float> getScale;
    private ITextStoreACPSink? sink;
    private ITfCompositionView? composition;
    private ITfCategoryMgr? categoryManager;
    private ITfDisplayAttributeMgr? displayAttributeManager;
    private bool attributesUnavailable;

    public TsfTextStore(
        Func<ITextInputClient?> getClient,
        Func<HWND> getWindow,
        Func<float> getScale)
    {
        this.getClient = getClient;
        this.getWindow = getWindow;
        this.getScale = getScale;
    }

    private ITextInputClient? Client => getClient();

    internal ITextInputClient? CurrentClient => Client;

    private string Text => Client?.Text ?? string.Empty;

    public void AttachContext(ITfContext context)
    {
    }

    public void ClearComposition()
    {
        composition = null;
        try
        {
            Client?.SetComposition(default, null);
        }
        catch
        {
        }
    }

    public void NotifyTextChanged(TextChange change)
    {
        if (sink is null)
        {
            return;
        }

        var nativeChange = new TS_TEXTCHANGE
        {
            acpStart = change.Start,
            acpOldEnd = change.Start + change.OldLength,
            acpNewEnd = change.Start + change.NewLength,
        };
        sink.OnTextChange(0, in nativeChange);
    }

    public void NotifySelectionChanged() => sink?.OnSelectionChange();

    public void NotifyLayoutChanged() => sink?.OnLayoutChange(TsLayoutCode.TS_LC_CHANGE, 0);

    public void AdviseSink(Guid* interfaceId, object unknown, uint mask) =>
        sink = unknown as ITextStoreACPSink;

    public void UnadviseSink(object unknown) => sink = null;

    public void RequestLock(uint lockFlags, HRESULT* sessionResult)
    {
        if (sink is null)
        {
            *sessionResult = new(unchecked((int)0x80004005));
            return;
        }

        try
        {
            sink.OnLockGranted((TEXT_STORE_LOCK_FLAGS)lockFlags);
            *sessionResult = new(0);
        }
        catch (COMException exception)
        {
            *sessionResult = new(exception.HResult);
        }
    }

    public void GetStatus(TS_STATUS* status)
    {
        status->dwDynamicFlags = 0;
        status->dwStaticFlags = 0;
    }

    public void QueryInsert(
        int testStart,
        int testEnd,
        uint characterCount,
        out int resultStart,
        out int resultEnd)
    {
        int length = Text.Length;
        resultStart = Math.Clamp(testStart, 0, length);
        resultEnd = Math.Clamp(testEnd, 0, length);
    }

    public void GetSelection(
        uint index,
        uint count,
        TS_SELECTION_ACP* selection,
        out uint fetched)
    {
        fetched = 0;
        if (count == 0)
        {
            return;
        }

        TextRange range = Client?.Selection ?? default;
        selection[0].acpStart = range.Start;
        selection[0].acpEnd = range.End;
        selection[0].style.ase = TsActiveSelEnd.TS_AE_END;
        selection[0].style.fInterimChar = false;
        fetched = 1;
    }

    public void SetSelection(uint count, TS_SELECTION_ACP* selection)
    {
        if (count > 0)
        {
            Client?.Select(new(selection[0].acpStart, selection[0].acpEnd - selection[0].acpStart));
        }
    }

    public void GetText(
        int start,
        int end,
        PWSTR plainText,
        uint requestedCharacterCount,
        out uint returnedCharacterCount,
        TS_RUNINFO* runInfo,
        uint requestedRunCount,
        out uint returnedRunCount,
        out int nextPosition)
    {
        string text = Text;
        int clampedStart = Math.Clamp(start, 0, text.Length);
        int clampedEnd = end < 0 ? text.Length : Math.Clamp(end, clampedStart, text.Length);
        int count = Math.Min(clampedEnd - clampedStart, (int)requestedCharacterCount);
        for (int index = 0; index < count; index++)
        {
            plainText.Value[index] = text[clampedStart + index];
        }

        returnedCharacterCount = (uint)count;
        returnedRunCount = 0;
        if (requestedRunCount > 0 && count > 0)
        {
            runInfo[0].uCount = (uint)count;
            runInfo[0].type = TsRunType.TS_RT_PLAIN;
            returnedRunCount = 1;
        }

        nextPosition = clampedStart + count;
    }

    public void SetText(
        uint flags,
        int start,
        int end,
        PCWSTR text,
        uint characterCount,
        TS_TEXTCHANGE* change)
    {
        string replacement = characterCount > 0
            ? new(text.Value, 0, (int)characterCount)
            : string.Empty;
        Client?.Replace(new(start, end - start), replacement);
        SetChange(change, start, end, start + replacement.Length);
    }

    public void InsertTextAtSelection(
        uint flags,
        PCWSTR text,
        uint characterCount,
        out int start,
        out int end,
        TS_TEXTCHANGE* change)
    {
        string replacement = characterCount > 0
            ? new(text.Value, 0, (int)characterCount)
            : string.Empty;
        TextRange selection = Client?.Selection ?? default;
        Client?.Replace(selection, replacement);
        start = selection.Start;
        end = selection.Start + replacement.Length;
        SetChange(change, selection.Start, selection.End, end);
    }

    public void GetEndACP(out int position) => position = Text.Length;

    public void GetActiveView(out uint view) => view = 0;

    public void GetWnd(uint view, HWND* window) => *window = getWindow();

    public void GetTextExt(
        uint view,
        int start,
        int end,
        RECT* bounds,
        BOOL* clipped)
    {
        if (Client?.CaretBounds is not RectangleF caret)
        {
            Fail(NoLayout);
            return;
        }

        float scale = getScale();
        var topLeft = new Point((int)(caret.Left * scale), (int)(caret.Top * scale));
        var bottomRight = new Point(
            (int)((caret.Left + MathF.Max(caret.Width, 1)) * scale),
            (int)(caret.Bottom * scale));
        HWND window = getWindow();
        PInvoke.ClientToScreen(window, ref topLeft);
        PInvoke.ClientToScreen(window, ref bottomRight);
        bounds->left = topLeft.X;
        bounds->top = topLeft.Y;
        bounds->right = bottomRight.X;
        bounds->bottom = bottomRight.Y;
        *clipped = false;
    }

    public void GetScreenExt(uint view, RECT* bounds)
    {
        HWND window = getWindow();
        PInvoke.GetClientRect(window, out RECT clientBounds);
        var topLeft = new Point(clientBounds.left, clientBounds.top);
        var bottomRight = new Point(clientBounds.right, clientBounds.bottom);
        PInvoke.ClientToScreen(window, ref topLeft);
        PInvoke.ClientToScreen(window, ref bottomRight);
        bounds->left = topLeft.X;
        bounds->top = topLeft.Y;
        bounds->right = bottomRight.X;
        bounds->bottom = bottomRight.Y;
    }

    public void GetACPFromPoint(uint view, Point* screenPoint, uint flags, out int position)
    {
        position = 0;
        Fail(NotImplemented);
    }

    public void RequestSupportedAttrs(uint flags, uint count, Guid* attributes)
    {
    }

    public void RequestAttrsAtPosition(int position, uint count, Guid* attributes, uint flags)
    {
    }

    public void RequestAttrsTransitioningAtPosition(
        int position,
        uint count,
        Guid* attributes,
        uint flags)
    {
    }

    public void FindNextAttrTransition(
        int start,
        int halt,
        uint count,
        Guid* attributes,
        uint flags,
        out int next,
        BOOL* found,
        out int foundOffset)
    {
        next = halt;
        *found = false;
        foundOffset = 0;
    }

    public void RetrieveRequestedAttrs(uint count, TS_ATTRVAL[] values, out uint fetched) => fetched = 0;

    public void OnStartComposition(ITfCompositionView composition, BOOL* accepted)
    {
        this.composition = composition;
        *accepted = true;
    }

    public void OnUpdateComposition(ITfCompositionView composition, ITfRange newRange) =>
        this.composition = composition;

    public void OnEndComposition(ITfCompositionView composition) => ClearComposition();

    public void OnEndEdit(ITfContext context, uint readOnlyCookie, ITfEditRecord editRecord)
    {
        try
        {
            PublishComposition(context, readOnlyCookie);
        }
        catch
        {
        }
    }

    public void GetFormattedText(int start, int end, out IDataObject dataObject)
    {
        dataObject = null!;
        Fail(NotImplemented);
    }

    public void GetEmbedded(int position, Guid* service, Guid* interfaceId, out object unknown)
    {
        unknown = null!;
        Fail(NotImplemented);
    }

    public void QueryInsertEmbedded(Guid* service, FORMATETC* format, BOOL* insertable) =>
        *insertable = false;

    public void InsertEmbedded(
        uint flags,
        int start,
        int end,
        IDataObject dataObject,
        TS_TEXTCHANGE* change) => Fail(NotImplemented);

    public void InsertEmbeddedAtSelection(
        uint flags,
        IDataObject dataObject,
        out int start,
        out int end,
        TS_TEXTCHANGE* change)
    {
        start = 0;
        end = 0;
        Fail(NotImplemented);
    }

    private static void SetChange(TS_TEXTCHANGE* change, int start, int oldEnd, int newEnd)
    {
        if (change is null)
        {
            return;
        }

        change->acpStart = start;
        change->acpOldEnd = oldEnd;
        change->acpNewEnd = newEnd;
    }

    private void PublishComposition(ITfContext context, uint cookie)
    {
        if (composition is not ITfCompositionView compositionView)
        {
            return;
        }

        compositionView.GetRange(out ITfRange range);
        if (range is not ITfRangeACP compositionRange)
        {
            return;
        }

        compositionRange.GetExtent(out int start, out int length);
        if (length <= 0)
        {
            Client?.SetComposition(default, null);
            return;
        }

        TextRange? target = attributesUnavailable
            ? null
            : ReadTargetSegment(context, cookie, range);
        Client?.SetComposition(new(start, length), target);
    }

    private TextRange? ReadTargetSegment(ITfContext context, uint cookie, ITfRange compositionRange)
    {
        try
        {
            EnsureAttributeManagers();
            if (categoryManager is null || displayAttributeManager is null)
            {
                attributesUnavailable = true;
                return null;
            }

            Guid propertyId = PInvoke.GUID_PROP_ATTRIBUTE;
            context.GetProperty(in propertyId, out ITfProperty property);
            property.EnumRanges(cookie, out IEnumTfRanges ranges, compositionRange);
            int targetStart = 0;
            int targetEnd = 0;
            var buffer = new ITfRange[1];
            while (true)
            {
                ranges.Next(1, buffer, out uint fetched);
                if (fetched == 0)
                {
                    break;
                }

                ITfRange range = buffer[0];
                property.GetValue(cookie, range, out object? value);
                if (value is not int atom || atom == 0)
                {
                    continue;
                }

                categoryManager.GetGUID((uint)atom, out Guid attributeId);
                displayAttributeManager.GetDisplayAttributeInfo(
                    in attributeId,
                    out ITfDisplayAttributeInfo? info,
                    out _);
                if (info is null)
                {
                    continue;
                }

                info.GetAttributeInfo(out TF_DISPLAYATTRIBUTE attribute);
                if (attribute.bAttr != TF_DA_ATTR_INFO.TF_ATTR_TARGET_CONVERTED
                    || range is not ITfRangeACP attributeRange)
                {
                    continue;
                }

                attributeRange.GetExtent(out int start, out int length);
                if (targetEnd == 0)
                {
                    targetStart = start;
                    targetEnd = start + length;
                }
                else
                {
                    targetStart = Math.Min(targetStart, start);
                    targetEnd = Math.Max(targetEnd, start + length);
                }
            }

            return targetEnd > targetStart
                ? new(targetStart, targetEnd - targetStart)
                : null;
        }
        catch
        {
            attributesUnavailable = true;
            return null;
        }
    }

    private void EnsureAttributeManagers()
    {
        if (categoryManager is not null)
        {
            return;
        }

        Guid categoryClassId = PInvoke.CLSID_TF_CategoryMgr;
        PInvoke.CoCreateInstance(
            in categoryClassId,
            null,
            CLSCTX.CLSCTX_INPROC_SERVER,
            out categoryManager);
        Guid displayAttributeClassId = PInvoke.CLSID_TF_DisplayAttributeMgr;
        PInvoke.CoCreateInstance(
            in displayAttributeClassId,
            null,
            CLSCTX.CLSCTX_INPROC_SERVER,
            out displayAttributeManager);
    }

    private static void Fail(int result) => throw new COMException(null, result);
}
