using System.Buffers.Binary;
using Lumyte.UI.Application.WebView.Cef;
using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Wire version and bounded-control admission regressions.</summary>
public sealed class InputJsProtocolTests
{
    /// <summary>Legacy header/Ready cannot silently advertise new capabilities.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task RejectLegacyHeaderAndReadyAsync()
    {
        using var stream = new MemoryStream();
        await CefHelperProtocol.WriteAsync(stream, new(101, Body: new byte[32]), default);
        byte[] bytes = stream.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => CefHelperProtocol.ReadAsync(new MemoryStream(bytes), default));
        using var shortReady = new MemoryStream();
        await CefHelperProtocol.WriteAsync(shortReady, new(101, Body: new byte[24]), default);
        shortReady.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => CefHelperProtocol.ReadAsync(shortReady, default));
    }

    /// <summary>Every command is bounded, while frames remain empty notices.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ControlsBoundedAndFramesEmptyAsync()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => CefHelperProtocol.WriteAsync(new MemoryStream(), new(10, Body: new byte[4097]), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => CefHelperProtocol.WriteAsync(new MemoryStream(), new(103, Body: new byte[4]), default));
        using var stream = new MemoryStream();
        await CefHelperProtocol.WriteAsync(stream, new(103, Width: 1, Height: 1), default);
        Assert.Equal(44, stream.Length);
        Assert.Equal(2UL, BinaryPrimitives.ReadUInt64LittleEndian(stream.ToArray().AsSpan(8)));
    }
}
