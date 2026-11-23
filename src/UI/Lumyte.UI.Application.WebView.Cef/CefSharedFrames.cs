using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Lumyte.UI.WebView;

namespace Lumyte.UI.Application.WebView.Cef;

// Session-owned mapping: Dispose only after the native cleanup receipt and reader join.
internal sealed unsafe class CefSharedFrames : IDisposable
{
    private readonly object _gate = new();
    private readonly WebViewPixelBudget _budget;
    private readonly long _reservation;
    private readonly byte[] _nonce = RandomNumberGenerator.GetBytes(16);
    private FileStream? _file;
    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private byte* _pointer;
    private long _lastFrameSequence;
    private bool _disposed;

    internal CefSharedFrames(string root, WebViewPixelBudget budget)
    {
        if (!OperatingSystem.IsLinux() || !BitConverter.IsLittleEndian || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
        {
            throw new PlatformNotSupportedException("cef-shared-platform");
        }

        _budget = budget;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            long available = budget.Limit - budget.Used;
            for (int candidate = CefSharedFrameLayout.MaximumCapacity; candidate >= 4096; candidate -= 4096)
            {
                long reserve = CefSharedFrameLayout.FileBytes(candidate) + CefSharedFrameLayout.ControlAllowance;
                if (reserve + (3L * candidate) <= available)
                {
                    if (budget.TryReserve(reserve))
                    {
                        Capacity = candidate;
                        _reservation = reserve;
                    }

                    break;
                }
            }

            if (Capacity != 0)
            {
                break;
            }
        }

        if (Capacity == 0)
        {
            throw new IOException("cef-shared-budget");
        }

        try
        {
            long bytes = CefSharedFrameLayout.FileBytes(Capacity);
            _file = new FileStream(Path.Combine(root, "frames-v1.shm"), new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.ReadWrite | FileShare.Delete,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                PreallocationSize = bytes,
            });
            _file.SetLength(bytes);
            _map = MemoryMappedFile.CreateFromFile(_file, null, bytes, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, true);
            _view = _map.CreateViewAccessor(0, bytes, MemoryMappedFileAccess.ReadWrite);
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _pointer);
            CefSharedFrameLayout.Initialize(new Span<byte>(_pointer, 4096), Capacity, _nonce);
            for (int i = 0; i < 3; i++)
            {
                Span<byte> slot = new(_pointer + SlotOffset(i), 64);
                slot.Clear();
                BinaryPrimitives.WriteInt32LittleEndian(slot[4..], i);
                _nonce.CopyTo(slot[40..56]);
                BinaryPrimitives.WriteUInt32LittleEndian(slot[56..], 1);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal int Capacity { get; }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_pointer != null)
            {
                _view!.SafeMemoryMappedViewHandle.ReleasePointer();
                _pointer = null;
            }

            _view?.Dispose();
            _map?.Dispose();
            _file?.Dispose();
            _budget.Release(_reservation);
        }
    }

    internal bool MatchesReady(byte[]? body) => body is { Length: 24 } && body.AsSpan(0, 8).SequenceEqual(CefSharedFrameLayout.Magic) && body.AsSpan(8).SequenceEqual(_nonce);

    internal void Rescan(WebViewPixelMailbox mailbox, long generation, int width, int height)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateGlobal();

            // Bounded contention retries, no polling or waits for the producer.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int selected = -1;
                long best = 0;
                for (int i = 0; i < 3; i++)
                {
                    byte* candidate = _pointer + SlotOffset(i);
                    if (Volatile.Read(ref *(int*)candidate) == 2)
                    {
                        long sequence = Interlocked.Read(ref *(long*)(candidate + 8));

                        // Metadata may race a Ready reclamation; validation belongs under Reading.
                        if (selected < 0 || sequence > best)
                        {
                            selected = i;
                            best = sequence;
                        }
                    }
                }

                if (selected < 0)
                {
                    return;
                }

                byte* pointer = _pointer + SlotOffset(selected);
                if (Interlocked.CompareExchange(ref *(int*)pointer, 3, 2) != 2)
                {
                    continue;
                }

                try
                {
                    ReadOnlySpan<byte> slot = new(pointer, 64);
                    long sequence = Interlocked.Read(ref *(long*)(pointer + 8));
                    long frameGeneration = BinaryPrimitives.ReadInt64LittleEndian(slot[16..]);
                    int frameWidth = BinaryPrimitives.ReadInt32LittleEndian(slot[24..]);
                    int frameHeight = BinaryPrimitives.ReadInt32LittleEndian(slot[28..]);
                    uint stride = BinaryPrimitives.ReadUInt32LittleEndian(slot[32..]);
                    uint count = BinaryPrimitives.ReadUInt32LittleEndian(slot[36..]);
                    long expected = frameWidth is > 0 and <= 4096 && frameHeight is > 0 and <= 4096 ? checked((long)frameWidth * frameHeight * 4) : -1;
                    if (BinaryPrimitives.ReadInt32LittleEndian(slot[4..]) != selected || sequence <= 0 || frameGeneration < 0 || frameGeneration > generation || frameWidth <= 0 || frameHeight <= 0 || frameWidth > 4096 || frameHeight > 4096 || stride != (long)frameWidth * 4 || count != expected || count > Capacity || SlotOffset(selected) + 64 + count > CefSharedFrameLayout.FileBytes(Capacity) || !slot[40..56].SequenceEqual(_nonce) || BinaryPrimitives.ReadUInt32LittleEndian(slot[56..]) != 1 || BinaryPrimitives.ReadUInt32LittleEndian(slot[60..]) != 0)
                    {
                        throw new InvalidDataException("cef-shared-slot");
                    }

                    if (frameGeneration == generation && (frameWidth != width || frameHeight != height))
                    {
                        throw new InvalidDataException("cef-shared-geometry");
                    }

                    if (sequence > _lastFrameSequence)
                    {
                        if (frameGeneration == generation)
                        {
                            // One intentional owned CPU copy. No shared span escapes the Reading lease.
                            mailbox.Publish(new ReadOnlySpan<byte>(pointer + 64, (int)count), frameWidth, frameHeight, frameGeneration);
                        }

                        _lastFrameSequence = sequence;
                    }
                }
                finally
                {
                    Volatile.Write(ref *(int*)pointer, 0);
                }
            }
        }
    }

    private long SlotOffset(int index) => 4096 + (index * CefSharedFrameLayout.SlotStride(Capacity));

    private void ValidateGlobal()
    {
        // Trusted same-UID owner must not truncate a live file; this check is not a defense
        // against malicious concurrent truncation between fstat and mapped access.
        if (_file!.Length != CefSharedFrameLayout.FileBytes(Capacity) || _view!.Capacity != CefSharedFrameLayout.FileBytes(Capacity))
        {
            throw new InvalidDataException("cef-shared-extent");
        }

        Span<byte> expected = stackalloc byte[4096];
        CefSharedFrameLayout.Initialize(expected, Capacity, _nonce);
        if (!new ReadOnlySpan<byte>(_pointer, 4096).SequenceEqual(expected))
        {
            throw new InvalidDataException("cef-shared-header");
        }
    }
}
