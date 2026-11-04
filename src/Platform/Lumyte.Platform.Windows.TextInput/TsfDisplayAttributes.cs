using System.Runtime.InteropServices;
using Lumyte.Platform.Windows.TextInput.Interop;
using Lumyte.TextEditing;
using Windows.Win32;

namespace Lumyte.Platform.Windows.TextInput;

internal static unsafe class TsfDisplayAttributes
{
    internal static IReadOnlyList<TextCompositionSpan> Read(nint contextPointer, uint cookie, nint compositionPointer, TextSpan composition)
    {
        ComPtr<ITfContext> context = TsfCom.Query<ITfContext>(contextPointer);
        ComPtr<ITfCompositionView> view = TsfCom.Query<ITfCompositionView>(compositionPointer);
        ComPtr<ITfProperty>? property = null;
        ComPtr<IEnumTfRanges>? ranges = null;
        ComPtr<ITfCategoryMgr>? categories = null;
        ComPtr<ITfDisplayAttributeMgr>? display = null;
        nint target = 0;
        try
        {
            Marshal.ThrowExceptionForHR(view.Pointer->GetRange((ITfRange**)&target));
            Guid attributeProperty = PInvoke.GUID_PROP_ATTRIBUTE;
            nint pointer;
            Marshal.ThrowExceptionForHR(context.Pointer->GetProperty(&attributeProperty, (ITfProperty**)&pointer));
            property = TsfCom.Own<ITfProperty>(pointer);
            Marshal.ThrowExceptionForHR(property.Pointer->EnumRanges(cookie, (IEnumTfRanges**)&pointer, (ITfRange*)target));
            ranges = TsfCom.Own<IEnumTfRanges>(pointer);
            categories = Create<ITfCategoryMgr>(PInvoke.CLSID_TF_CategoryMgr);
            display = Create<ITfDisplayAttributeMgr>(PInvoke.CLSID_TF_DisplayAttributeMgr);
            var result = new List<TextCompositionSpan>();
            VARIANT variant = default;
            int rangeCount = 0;
            while (true)
            {
                uint fetched;
                nint rangePointer = 0;
                int next = ranges.Pointer->Next(1, (ITfRange**)&rangePointer, &fetched);
                Marshal.ThrowExceptionForHR(next);
                if (fetched == 0)
                {
                    break;
                }

                if (++rangeCount > 256)
                {
                    TsfCom.Return(rangePointer);
                    throw new InvalidDataException("The composition attribute limit was exceeded.");
                }

                try
                {
                    ComPtr<ITfRangeACP> range = TsfCom.Query<ITfRangeACP>(rangePointer);
                    int start;
                    int length;
                    try
                    {
                        Marshal.ThrowExceptionForHR(range.Pointer->GetExtent(&start, &length));
                    }
                    finally
                    {
                        TsfCom.Release(range);
                    }

                    int clippedStart = Math.Max(start, composition.Start);
                    int clippedEnd = Math.Min(checked(start + length), checked(composition.Start + composition.Length));
                    if (clippedEnd <= clippedStart)
                    {
                        continue;
                    }

                    variant = default;
                    try
                    {
                        Marshal.ThrowExceptionForHR(property.Pointer->GetValue(cookie, (ITfRange*)rangePointer, &variant));
                        if (variant.vt != VARENUM.VT_I4)
                        {
                            continue;
                        }

                        Guid guid;
                        Marshal.ThrowExceptionForHR(categories.Pointer->GetGUID(unchecked((uint)variant.lVal), &guid));
                        nint infoPointer;
                        Marshal.ThrowExceptionForHR(display.Pointer->GetDisplayAttributeInfo(&guid, (ITfDisplayAttributeInfo**)&infoPointer, null));
                        ComPtr<ITfDisplayAttributeInfo> info = TsfCom.Own<ITfDisplayAttributeInfo>(infoPointer);
                        try
                        {
                            TfDisplayAttribute value;
                            Marshal.ThrowExceptionForHR(info.Pointer->GetAttributeInfo((TF_DISPLAYATTRIBUTE*)&value));
                            TextUnderline underline = (int)value.lsStyle switch
                            {
                                0 => TextUnderline.None,
                                2 or 3 or 4 => TextUnderline.Dotted,
                                _ => value.fBoldLine.Value != 0 ? TextUnderline.Thick : TextUnderline.Solid,
                            };
                            result.Add(new(new(clippedStart, clippedEnd - clippedStart), underline, (int)value.bAttr is 1 or 3));
                        }
                        finally
                        {
                            TsfCom.Release(info);
                        }
                    }
                    finally
                    {
                        PInvoke.VariantClear(&variant);
                    }
                }
                finally
                {
                    TsfCom.Return(rangePointer);
                }
            }

            return result.Count == 0 ? [new(composition, TextUnderline.Solid, false)] : result.AsReadOnly();
        }
        finally
        {
            TsfCom.Release(display);
            TsfCom.Release(categories);
            TsfCom.Release(ranges);
            TsfCom.Release(property);
            TsfCom.Return(target);
            TsfCom.Release(view);
            TsfCom.Release(context);
        }
    }

    private static ComPtr<T> Create<T>(Guid clsid)
        where T : unmanaged, global::Windows.Win32.IComIID
    {
        Guid iid = T.Guid;
        Marshal.ThrowExceptionForHR(TsfNative.CoCreateInstance(in clsid, 0, 1, in iid, out nint pointer));
        return TsfCom.Own<T>(pointer);
    }
}
