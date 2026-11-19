using System.Buffers.Binary;

namespace Lumyte.UI.Application.WebView.Cef;

// Backend-local v2 bounded control protocol. Pixel SHM remains independently versioned; no native pointers.
internal static class CefHelperProtocol
{
    internal const int MaximumBody = 16 * 1024 * 1024;

    internal static async Task WriteAsync(Stream stream, CefHelperPacket packet, CancellationToken token)
    {
        byte[] body = packet.Body ?? [];
        if (body.Length > 4096 || (packet.Kind == 103 && body.Length != 0))
        {
            throw new InvalidDataException("cef-command-too-large");
        }

        byte[] header = new byte[44];
        BinaryPrimitives.WriteInt32LittleEndian(header, 40 + body.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), packet.Kind);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), 2);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), packet.Generation);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(24), packet.Sequence);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(32), packet.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(36), packet.Height);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), body.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }

    internal static async Task<CefHelperPacket> ReadAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[44];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        int bodyLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(40));
        if (BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8)) == 1)
        {
            throw new InvalidDataException("cef-helper-protocol-incompatible");
        }

        if (length < 40 || length > 4096 + 40 || bodyLength != length - 40 || BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8)) != 2)
        {
            throw new InvalidDataException("cef-protocol-header");
        }

        uint kind = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        if (BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16)) < 0)
        {
            throw new InvalidDataException("cef-protocol-generation");
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(32));
        int height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(36));
        if (kind == 103 && (width <= 0 || height <= 0 || width > 4096 || height > 4096 || bodyLength != 0))
        {
            throw new InvalidDataException("cef-protocol-frame");
        }

        if ((kind == 101 && bodyLength != 32) || (kind != 103 && bodyLength > 4096))
        {
            throw new InvalidDataException("cef-protocol-state");
        }

        byte[] body = new byte[bodyLength];
        await stream.ReadExactlyAsync(body, token);
        return new(kind, BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16)), BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(24)), width, height, body);
    }
}
