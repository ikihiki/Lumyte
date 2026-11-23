using System.Buffers.Binary;

namespace Lumyte.UI.Application.WebView.Cef;

// shm-v1: Linux little-endian x64/aarch64. No process-local pointers in the ABI.
internal static class CefSharedFrameLayout
{
    internal const int HeaderBytes = 4096;
    internal const int SlotHeaderBytes = 64;
    internal const int Slots = 3;
    internal const int MaximumCapacity = 16 * 1024 * 1024;
    internal const int ControlAllowance = 64 * 1024;

    internal static ReadOnlySpan<byte> Magic => "LUMYSHM1"u8;

    internal static long SlotStride(int capacity) => checked(((long)capacity + 64 + 4095) / 4096 * 4096);

    internal static long FileBytes(int capacity) => checked(HeaderBytes + (Slots * SlotStride(capacity)));

    internal static void Initialize(Span<byte> header, int capacity, ReadOnlySpan<byte> nonce)
    {
        header.Clear();
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], HeaderBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], Slots);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], SlotHeaderBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(header[24..], (ulong)capacity);
        BinaryPrimitives.WriteUInt64LittleEndian(header[32..], (ulong)SlotStride(capacity));
        BinaryPrimitives.WriteUInt64LittleEndian(header[40..], (ulong)FileBytes(capacity));
        nonce.CopyTo(header[48..64]);
    }
}
