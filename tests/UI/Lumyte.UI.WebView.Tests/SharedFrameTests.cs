using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using Lumyte.UI.Application.WebView.Cef;
using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Frozen byte ABI and actual mmap leases, independent of CEF.</summary>
public sealed class SharedFrameTests
{
    /// <summary>Fixed offsets have the independently computed golden bytes on every OS.</summary>
    [Fact]
    public void GlobalGoldenBytes()
    {
        byte[] bytes = new byte[4096];
        byte[] nonce = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        CefSharedFrameLayout.Initialize(bytes, 4096, nonce);
        Assert.Equal("4C554D5953484D3101000000001000000300000040000000001000000000000000200000000000000070000000000000000102030405060708090A0B0C0D0E0F", Convert.ToHexString(bytes.AsSpan(0, 64)));
        Assert.All(bytes.Skip(64), b => Assert.Equal(0, b));
        Assert.Equal(8192, CefSharedFrameLayout.SlotStride(4096));
        Assert.Equal(28672, CefSharedFrameLayout.FileBytes(4096));
    }

    /// <summary>Complete map matches the independently emitted Rust golden artifact digest.</summary>
    [Fact]
    public void RustGoldenMapDigest()
    {
        byte[] map = new byte[28672];
        byte[] nonce = Enumerable.Repeat((byte)0x42, 16).ToArray();
        CefSharedFrameLayout.Initialize(map.AsSpan(0, 4096), 4096, nonce);
        for (int i = 0; i < 3; i++)
        {
            Span<byte> slot = map.AsSpan(4096 + (i * 8192), 64);
            BinaryPrimitives.WriteInt32LittleEndian(slot[4..], i);
            nonce.CopyTo(slot[40..56]);
            BinaryPrimitives.WriteUInt32LittleEndian(slot[56..], 1);
        }

        Assert.Equal("37A3652263B2FF9CE1BDE5E9FBE8BB4A284B857A4016F1888C8F4B8BDB6A1585", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(map)));
    }

    /// <summary>Notice packets contain no pixel allocation and retain the 44-byte wire bound.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task NoticeIsEmptyAndLegacyPixelsRejectedAsync()
    {
        using var stream = new MemoryStream();
        await CefHelperProtocol.WriteAsync(stream, new(103, 7, 99, 2, 2), default);
        Assert.Equal(44, stream.Length);
        stream.Position = 0;
        Assert.Empty((await CefHelperProtocol.ReadAsync(stream, default)).Body!);
        byte[] header = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(header, 40 + (16 * 1024 * 1024));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), 16 * 1024 * 1024);
        await Assert.ThrowsAsync<InvalidDataException>(() => CefHelperProtocol.ReadAsync(new MemoryStream(header), default));
    }

    /// <summary>Actual injected budget includes padding, control allowance and held owned CPU copies.</summary>
    [CefHelperFact]
    public void BudgetAndLatestReadySelection()
    {
        using var fixture = new Ring();
        long reserved = CefSharedFrameLayout.FileBytes(fixture.Reader.Capacity) + 65536;
        Assert.Equal(reserved, fixture.Budget.Used);
        Assert.True(reserved + (3L * fixture.Reader.Capacity) <= 1048576);
        Assert.True(CefSharedFrameLayout.FileBytes(fixture.Reader.Capacity + 4096) + 65536 + (3L * (fixture.Reader.Capacity + 4096)) > 1048576);
        for (byte i = 1; i <= 12; i++)
        {
            Assert.True(fixture.Producer.Publish(2, 2, 0, i));
        }

        fixture.Reader.Rescan(fixture.Mailbox, 0, 2, 2);
        using WebViewPixelMailbox.PixelLease? lease = fixture.Mailbox.Take();
        Assert.NotNull(lease);
        Assert.All(lease.Bytes, b => Assert.Equal(12, b));
        fixture.Reader.Dispose();
        Assert.Equal(16, fixture.Budget.Used);
        lease.Dispose();
        Assert.Equal(0, fixture.Budget.Used);
    }

    /// <summary>Reading slots remain immutable through wrap; all-three-held drops without waiting.</summary>
    [CefHelperFact]
    public void HeldReadingLeasesNeverReclaimed()
    {
        using var fixture = new Ring();
        fixture.Producer.Publish(2, 2, 0, 42);
        fixture.View.Write(4096, 3);
        for (int i = 0; i < 50; i++)
        {
            Assert.True(fixture.Producer.Publish(2, 2, 0, 99));
        }

        Assert.Equal(42, fixture.View.ReadByte(4160));
        for (int i = 0; i < 3; i++)
        {
            fixture.View.Write(4096 + (i * CefSharedFrameLayout.SlotStride(fixture.Reader.Capacity)), 3);
        }

        Assert.False(fixture.Producer.Publish(2, 2, 0));
        Assert.Equal(42, fixture.View.ReadByte(4160));
    }

    /// <summary>Malformed under-lease metadata is rejected and the slot always released.</summary>
    /// <param name="offset">Slot field offset.</param>
    /// <param name="value">Invalid little-endian field value.</param>
    [CefHelperTheory]
    [InlineData(4, 8)]
    [InlineData(16, -1)]
    [InlineData(16, 1)]
    [InlineData(24, int.MaxValue)]
    [InlineData(28, int.MaxValue)]
    [InlineData(32, 999)]
    [InlineData(36, int.MaxValue)]
    [InlineData(40, 123)]
    [InlineData(56, 2)]
    [InlineData(60, 1)]
    [InlineData(8, 0)]
    [InlineData(12, int.MinValue)]
    [InlineData(20, int.MaxValue)]
    public void MalformedSlotRejected(int offset, int value)
    {
        using var fixture = new Ring();
        fixture.Producer.Publish(2, 2, 0);
        fixture.View.Write(4096 + offset, value);
        Assert.Throws<InvalidDataException>(() => fixture.Reader.Rescan(fixture.Mailbox, 0, 2, 2));
        Assert.Equal(0, fixture.View.ReadInt32(4096));
    }

    /// <summary>Global capacity/padding/nonce changes cannot redirect reads.</summary>
    /// <param name="offset">Global header offset.</param>
    [CefHelperTheory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(40)]
    [InlineData(48)]
    [InlineData(100)]
    public void MalformedGlobalRejected(int offset)
    {
        using var fixture = new Ring();
        fixture.View.Write(offset, (byte)(fixture.View.ReadByte(offset) ^ 0xff));
        Assert.Throws<InvalidDataException>(() => fixture.Reader.Rescan(fixture.Mailbox, 0, 2, 2));
    }

    /// <summary>Old generations free their ring lease without resurrecting old owned pixels.</summary>
    [CefHelperFact]
    public void StaleGenerationAndReadyNonce()
    {
        using var fixture = new Ring();
        Assert.True(fixture.Reader.MatchesReady(fixture.Producer.ReadyBody));
        byte[] stale = fixture.Producer.ReadyBody;
        stale[23] ^= 1;
        Assert.False(fixture.Reader.MatchesReady(stale));
        Assert.False(fixture.Reader.MatchesReady([]));
        fixture.Producer.Publish(2, 2, 0);
        fixture.Mailbox.Resize(1);
        fixture.Reader.Rescan(fixture.Mailbox, 1, 3, 3);
        Assert.Null(fixture.Mailbox.Take());
        fixture.Producer.Publish(3, 3, 1);
        fixture.Reader.Rescan(fixture.Mailbox, 1, 3, 3);
        using WebViewPixelMailbox.PixelLease? current = fixture.Mailbox.Take();
        Assert.Equal(1, current!.Generation);
    }

    /// <summary>An insufficient budget never creates a mapped file or leaks a reservation.</summary>
    [CefHelperFact]
    public void InsufficientBudgetDoesNotSpill()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var budget = new WebViewPixelBudget(65536);
            Assert.Throws<IOException>(() => new CefSharedFrames(root, budget));
            Assert.Equal(0, budget.Used);
            Assert.Empty(Directory.GetFiles(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Exclusive creation failures return the reservation without touching an existing file.</summary>
    [CefHelperFact]
    public void ExclusiveCreationFailureReturnsBudget()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "frames-v1.shm");
            File.WriteAllText(path, "retained");
            var budget = new WebViewPixelBudget(1048576);
            Assert.Throws<IOException>(() => new CefSharedFrames(root, budget));
            Assert.Equal(0, budget.Used);
            Assert.Equal("retained", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>The producer emits exact independent slot offsets and keeps data directly after byte 64.</summary>
    [CefHelperFact]
    public void SlotGoldenBytes()
    {
        using var fixture = new Ring();
        fixture.Producer.Publish(2, 2, 7, 42);
        byte[] slot = new byte[80];
        fixture.View.ReadArray(4096, slot, 0, slot.Length);
        Assert.Equal("02000000000000000100000000000000070000000000000002000000020000000800000010000000", Convert.ToHexString(slot.AsSpan(0, 40)));
        Assert.Equal(fixture.Producer.ReadyBody.AsSpan(8).ToArray(), slot.AsSpan(40, 16).ToArray());
        Assert.Equal("0100000000000000", Convert.ToHexString(slot.AsSpan(56, 8)));
        Assert.All(slot.Skip(64), b => Assert.Equal(42, b));
    }

    /// <summary>Concurrent wrap cannot modify bytes during the reader's owned-copy operation.</summary>
    /// <returns>The verification.</returns>
    [CefHelperFact]
    public async Task ConcurrentWrapNeverTearsOwnedCopyAsync()
    {
        using var fixture = new Ring();
        var producer = Task.Run(() =>
        {
            for (int i = 1; i <= 2000; i++)
            {
                fixture.Producer.Publish(64, 64, 0, (byte)(i % 251));
            }
        });
        int copied = 0;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!producer.IsCompleted)
        {
            fixture.Reader.Rescan(fixture.Mailbox, 0, 64, 64);
            using WebViewPixelMailbox.PixelLease? lease = fixture.Mailbox.Take();
            if (lease is not null)
            {
                copied++;
                byte first = lease.Bytes[0];
                Assert.All(lease.Bytes, b => Assert.Equal(first, b));
            }

            deadline.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        await producer.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Reader.Rescan(fixture.Mailbox, 0, 64, 64);
        using WebViewPixelMailbox.PixelLease? final = fixture.Mailbox.Take();
        Assert.True(copied > 0 || final is not null);
        if (final is not null)
        {
            byte first = final.Bytes[0];
            Assert.All(final.Bytes, b => Assert.Equal(first, b));
        }
    }

    /// <summary>Capacity uses currently consumed injected bytes, not option defaults.</summary>
    [CefHelperFact]
    public void AlreadyUsedBudgetReducesCapacity()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var budget = new WebViewPixelBudget(1048576);
        Assert.True(budget.TryReserve(524288));
        try
        {
            using var reader = new CefSharedFrames(root, budget);
            Assert.True(reader.Capacity < 100000);
            Assert.True(budget.Used + (3L * reader.Capacity) <= budget.Limit);
        }
        finally
        {
            budget.Release(524288);
            Assert.Equal(0, budget.Used);
            Directory.Delete(root, true);
        }
    }

    private sealed class Ring : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        private readonly MemoryMappedFile _map;

        internal Ring()
        {
            Directory.CreateDirectory(_root);
            Reader = new(_root, Budget);
            Producer = new(_root);
            _map = MemoryMappedFile.CreateFromFile(Path.Combine(_root, "frames-v1.shm"), FileMode.Open);
            View = _map.CreateViewAccessor();
            Mailbox = new(Budget);
        }

        internal WebViewPixelBudget Budget { get; } = new(1048576);

        internal CefSharedFrames Reader { get; }

        internal FakeSharedFrameProducer Producer { get; }

        internal MemoryMappedViewAccessor View { get; }

        internal WebViewPixelMailbox Mailbox { get; }

        public void Dispose()
        {
            Mailbox.Dispose();
            Producer.Dispose();
            View.Dispose();
            _map.Dispose();
            Reader.Dispose();
            Assert.Equal(0, Budget.Used);
            Directory.Delete(_root, true);
        }
    }
}
