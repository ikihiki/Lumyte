using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Validates callback copies and bounded cross-thread ownership.</summary>
public sealed class PixelMailboxTests
{
    /// <summary>Replacing callback storage cannot overwrite the owner lease.</summary>
    [Fact]
    public void CallbackStorageIsCopiedAndReaderIsIsolated()
    {
        var budget = new WebViewPixelBudget(8);
        using var mailbox = new WebViewPixelMailbox(budget);
        byte[] callback = [1, 2, 3, 4];
        Assert.True(mailbox.Publish(callback, 1, 1, 0));
        Array.Fill(callback, (byte)0);
        using WebViewPixelMailbox.PixelLease first = mailbox.Take()!;
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, first.Bytes);
        Assert.True(mailbox.Publish(callback, 1, 1, 0));
        Assert.Equal(8, budget.Used);
        Assert.Equal((byte)1, first.Bytes[0]);
        mailbox.Dispose();
        Assert.Equal(4, budget.Used);
        first.Dispose();
        Assert.Equal(0, budget.Used);
    }

    /// <summary>Retained readers count against the same budget as pending callbacks.</summary>
    [Fact]
    public void BudgetCannotBeHiddenByReplacingLatest()
    {
        var budget = new WebViewPixelBudget(4);
        using var mailbox = new WebViewPixelMailbox(budget);
        Assert.True(mailbox.Publish(new byte[4], 1, 1, 0));
        using WebViewPixelMailbox.PixelLease reader = mailbox.Take()!;
        Assert.False(mailbox.Publish(new byte[4], 1, 1, 0));
        reader.Dispose();
        Assert.True(mailbox.Publish(new byte[4], 1, 1, 0));
    }

    /// <summary>Resize drops pending old pixels and close rejects callbacks.</summary>
    [Fact]
    public void ResizeAndCloseRejectStaleCallbacks()
    {
        var budget = new WebViewPixelBudget(16);
        using var mailbox = new WebViewPixelMailbox(budget);
        Assert.True(mailbox.Publish(new byte[4], 1, 1, 0));
        mailbox.Resize(1);
        Assert.Null(mailbox.Take());
        Assert.Equal(0, budget.Used);
        Assert.False(mailbox.Publish(new byte[4], 1, 1, 0));
        Assert.True(mailbox.Publish(new byte[4], 1, 1, 1));
        mailbox.Dispose();
        Assert.False(mailbox.Publish(new byte[4], 1, 1, 1));
        Assert.Equal(0, budget.Used);
    }
}
