using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Checks CPU payload accounting with explicitly delayed fake consumers.</summary>
public sealed class PixelRetirementTests
{
    /// <summary>Repeated replacement retains only the latest pending frame.</summary>
    [Fact]
    public void ReplacingLatestNeverAccumulatesPendingPayload()
    {
        var budget = new WebViewPixelBudget(4);
        using var mailbox = new WebViewPixelMailbox(budget);
        for (byte revision = 0; revision < 100; revision++)
        {
            Assert.True(mailbox.Publish(new byte[] { revision, 0, 0, 255 }, 1, 1, 0));
            Assert.Equal(4, budget.Used);
        }

        using WebViewPixelMailbox.PixelLease latest = mailbox.Take()!;
        Assert.Equal((byte)99, latest.Bytes[0]);
        Assert.Equal(1, latest.Width);
        Assert.Equal(1, latest.Height);
        Assert.Null(mailbox.Take());
        latest.Dispose();
        latest.Dispose();
        Assert.Equal(0, budget.Used);
    }

    /// <summary>Resize storms cannot release a reader or hide its charge from another session.</summary>
    [Fact]
    public void SharedBudgetIncludesRetiringReadersAcrossSessions()
    {
        var budget = new WebViewPixelBudget(8);
        using var first = new WebViewPixelMailbox(budget);
        using var second = new WebViewPixelMailbox(budget);
        Assert.True(first.Publish(new byte[4], 1, 1, 0));
        using WebViewPixelMailbox.PixelLease reader = first.Take()!;
        for (long generation = 1; generation <= 100; generation++)
        {
            first.Resize(generation);
            Assert.False(first.Publish(new byte[4], 1, 1, generation - 1));
            Assert.True(second.Publish(new byte[4], 1, 1, 0));
            Assert.Equal(8, budget.Used);
            Assert.False(first.Publish(new byte[4], 1, 1, generation));
        }

        first.Dispose();
        Assert.Equal(8, budget.Used);
        second.Dispose();
        Assert.Equal(4, budget.Used);
        Assert.Equal(0, reader.Generation);
        reader.Dispose();
        Assert.Equal(0, budget.Used);
    }

    /// <summary>Invalid dimensions or callback payload cannot leak a reservation.</summary>
    /// <param name="width">The supplied width.</param>
    /// <param name="height">The supplied height.</param>
    /// <param name="length">The callback byte length.</param>
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(-1, 1, 4)]
    [InlineData(1, 1, 3)]
    [InlineData(1, 1, 5)]
    public void InvalidPayloadDoesNotReserve(int width, int height, int length)
    {
        var budget = new WebViewPixelBudget(16);
        using var mailbox = new WebViewPixelMailbox(budget);
        Assert.Throws<ArgumentOutOfRangeException>(() => mailbox.Publish(new byte[length], width, height, 0));
        Assert.Equal(0, budget.Used);
        Assert.Null(mailbox.Take());
    }

    /// <summary>Dimension multiplication is checked before allocating a buffer.</summary>
    [Fact]
    public void OverflowDoesNotAllocateOrReserve()
    {
        var budget = new WebViewPixelBudget(long.MaxValue);
        using var mailbox = new WebViewPixelMailbox(budget);
        Assert.Throws<OverflowException>(() => mailbox.Publish([], int.MaxValue, int.MaxValue, 0));
        Assert.Equal(0, budget.Used);
    }

    /// <summary>Closed mailboxes never accept a late callback even after a resize notification.</summary>
    [Fact]
    public void DisposeIsIdempotentAndResizeCannotReopen()
    {
        var budget = new WebViewPixelBudget(4);
        using var mailbox = new WebViewPixelMailbox(budget);
        Assert.True(mailbox.Publish(new byte[4], 1, 1, 0));
        mailbox.Dispose();
        mailbox.Dispose();
        mailbox.Resize(1);
        Assert.False(mailbox.Publish(new byte[4], 1, 1, 1));
        Assert.Null(mailbox.Take());
        Assert.Equal(0, budget.Used);
    }
}
