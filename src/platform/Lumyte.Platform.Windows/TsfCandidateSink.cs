using System.Runtime.InteropServices;

using Lumyte.Input;
using Windows.Win32.Foundation;
using Windows.Win32.UI.TextServices;

namespace Lumyte.Platform.Windows;

internal sealed unsafe class TsfCandidateSink(
    ITfUIElementMgr manager,
    Func<ITextInputClient?> getClient) : ITfUIElementSink
{
    private uint? activeElementId;

    public void BeginUIElement(uint elementId, BOOL* show)
    {
        try
        {
            if (!TryGetCandidates(elementId, out ITfCandidateListUIElement candidates))
            {
                *show = true;
                return;
            }

            ITextInputClient? client = getClient();
            if (client is null)
            {
                *show = true;
                return;
            }

            activeElementId = elementId;
            *show = client.CandidatePresentation == TextCandidatePresentation.System;
            Publish(client, candidates);
        }
        catch
        {
            *show = true;
            Clear();
        }
    }

    public void UpdateUIElement(uint elementId)
    {
        if (activeElementId != elementId)
        {
            return;
        }

        try
        {
            if (getClient() is { } client
                && TryGetCandidates(elementId, out ITfCandidateListUIElement candidates))
            {
                Publish(client, candidates);
            }
        }
        catch
        {
            Clear();
        }
    }

    public void EndUIElement(uint elementId)
    {
        if (activeElementId == elementId)
        {
            Clear();
        }
    }

    private bool TryGetCandidates(uint elementId, out ITfCandidateListUIElement candidates)
    {
        manager.GetUIElement(elementId, out ITfUIElement element);
        if (element is ITfCandidateListUIElement candidateList)
        {
            candidates = candidateList;
            return true;
        }

        candidates = null!;
        return false;
    }

    private static void Publish(ITextInputClient client, ITfCandidateListUIElement candidates)
    {
        candidates.GetCount(out uint count);
        candidates.GetSelection(out uint selection);
        candidates.GetCurrentPage(out uint currentPage);

        candidates.GetPageIndex(null, 0, out uint pageCount);
        uint[] pageIndices = new uint[pageCount];
        if (pageIndices.Length > 0)
        {
            fixed (uint* indices = pageIndices)
            {
                candidates.GetPageIndex(indices, (uint)pageIndices.Length, out pageCount);
            }
        }

        var items = new string[count];
        for (uint index = 0; index < count; index++)
        {
            BSTR value = default;
            try
            {
                candidates.GetString(index, &value);
                items[index] = value.ToString() ?? string.Empty;
            }
            finally
            {
                if (value.Value is not null)
                {
                    Marshal.FreeBSTR(value);
                }
            }
        }

        int pageStart = currentPage < pageIndices.Length
            ? checked((int)pageIndices[currentPage])
            : 0;
        int pageEnd = currentPage + 1 < pageIndices.Length
            ? checked((int)pageIndices[currentPage + 1])
            : checked((int)count);
        client.SetCandidates(new()
        {
            Items = items,
            SelectedIndex = checked((int)selection),
            PageStart = pageStart,
            PageSize = Math.Max(0, pageEnd - pageStart),
        });
    }

    private void Clear()
    {
        activeElementId = null;
        try
        {
            getClient()?.SetCandidates(null);
        }
        catch
        {
        }
    }
}
