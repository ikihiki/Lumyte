using System.Diagnostics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal sealed class TransferSlot : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly bool _textureMode;
    private readonly bool _dispersed;
    private readonly IGraphicsBuffer<byte> _upload;
    private readonly IGraphicsBuffer<byte> _readback;
    private readonly IGraphicsBuffer<byte>? _gpu;
    private readonly IGraphicsTexture? _texture;
    private readonly HardwareGpuTimer _timer;
    private IGraphicsCommandBuffer? _commands;
    private IGraphicsSubmission? _submission;
    private bool _unknown;
    private bool _initialized;
    private long _submitted;

    internal TransferSlot(VulkanDevice device, bool textureMode, bool dispersed)
    {
        _device = device;
        _textureMode = textureMode;
        _dispersed = dispersed;
        TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb);
        uint alignment = Lcm(device.Caps.CopyBytesPerRowAlignment, layout.BytesPerRowAlignment);
        Pitch = ((512 + alignment - 1) / alignment) * alignment;
        if (4096 % device.Caps.CopyBufferOffsetAlignment != 0 || 4096 % device.Caps.CopyBufferSizeAlignment != 0 || ((ulong)Pitch * 8) % layout.BufferOffsetAlignmentInBytes != 0)
        {
            throw new NotSupportedException("Fixed transfer profile does not meet reported copy alignment.");
        }

        ulong count = textureMode ? (ulong)Pitch * 128 : 65536;
        _upload = device.CreateBuffer<byte>(new() { Count = count, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        _readback = device.CreateBuffer<byte>(new() { Count = count, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        if (textureMode)
        {
            _texture = device.CreateTexture(new() { Width = 128, Height = 128, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.CopySource | TextureUsage.CopyDestination });
        }
        else
        {
            _gpu = device.CreateBuffer<byte>(new() { Count = 65536, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination, Memory = MemoryPreference.Automatic });
        }

        _timer = new(device);
    }

    internal uint Pitch { get; }

    internal double MapCopyMs { get; private set; }

    internal double EncodeMs { get; private set; }

    internal double SubmitMs { get; private set; }

    internal double CompletionMs { get; private set; }

    internal double? GpuMs { get; private set; }

    internal bool Pending => _submission is not null;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_unknown || Pending)
        {
            PublicRetainedApiProbe.QuarantinedObjects.Add(this);
            return;
        }

        _commands?.Dispose();
        _timer.Dispose();
        _gpu?.Dispose();
        _texture?.Dispose();
        _readback.Dispose();
        _upload.Dispose();
    }

    internal static byte[] Expected(bool texture, bool dispersed, int frame)
    {
        byte[] bytes = new byte[65536];
        byte value = (frame & 1) == 0 ? (byte)0x39 : (byte)0xc7;
        for (int i = 0; i < bytes.Length; i++)
        {
            int unit = texture ? i / 512 / 8 : i / 4096;
            bool selected = dispersed ? unit % 4 == 0 : unit < 4;
            if (selected)
            {
                bytes[i] = value;
            }
        }

        return bytes;
    }

    internal byte[] Pad(byte[] packed)
    {
        if (!_textureMode)
        {
            return (byte[])packed.Clone();
        }

        byte[] padded = new byte[checked((int)Pitch * 128)];
        for (int y = 0; y < 128; y++)
        {
            packed.AsSpan(y * 512, 512).CopyTo(padded.AsSpan(y * checked((int)Pitch), 512));
        }

        return padded;
    }

    internal async Task SubmitAsync(byte[] source, bool initialize = false)
    {
        if (Pending || _unknown)
        {
            throw new InvalidOperationException("Transfer slot reused while GPU owns it.");
        }

        long begin = Stopwatch.GetTimestamp();
        await _upload.MapAsync();
        _upload.CopyFrom(source);
        _upload.Unmap();
        MapCopyMs = Ms(begin);
        begin = Stopwatch.GetTimestamp();
        _commands = _device.CreateCommandBuffer(new());
        if (_timer.Supported)
        {
            _timer.Begin(_commands);
        }

        _commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _readback.SliceElements(0, _readback.Count), Before = _initialized ? new(PipelineStage.Host, ResourceAccess.HostRead) : default, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
        _commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _upload.SliceElements(0, _upload.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        if (_textureMode)
        {
            _commands.Barrier(new TextureBarrierDesc { Texture = _texture!, Range = new(0, 1, 0, 1), BeforeState = _initialized ? TextureState.CopySource : TextureState.Undefined, AfterState = TextureState.CopyDestination, Before = _initialized ? new(PipelineStage.Copy, ResourceAccess.CopyRead) : default, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
            for (int part = 0; part < (initialize ? 1 : 4); part++)
            {
                uint row = initialize ? 0u : (uint)(part * (_dispersed ? 32 : 8));
                uint height = initialize ? 128u : 8u;
                _commands.CopyBufferToTexture(new() { Buffer = _upload.SliceElements(row * Pitch, (ulong)Pitch * height), BytesPerRow = Pitch, RowsPerImage = height }, new() { Texture = _texture!, OriginY = row, Width = 128, Height = height });
            }

            _commands.Barrier(new TextureBarrierDesc { Texture = _texture!, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopyDestination, AfterState = TextureState.CopySource, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            _commands.CopyTextureToBuffer(new() { Texture = _texture!, Width = 128, Height = 128 }, new() { Buffer = _readback.SliceElements(0, _readback.Count), BytesPerRow = Pitch, RowsPerImage = 128 });
        }
        else
        {
            _commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _gpu!.SliceElements(0, 65536), Before = _initialized ? new(PipelineStage.Copy, ResourceAccess.CopyRead) : default, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
            for (int part = 0; part < (initialize ? 1 : 4); part++)
            {
                ulong offset = initialize ? 0 : (ulong)(part * (_dispersed ? 4 : 1) * 4096);
                ulong length = initialize ? 65536UL : 4096UL;
                _commands.CopyBuffer(_upload.SliceElements(offset, length), _gpu.SliceElements(offset, length));
            }

            _commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _gpu.SliceElements(0, 65536), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            _commands.CopyBuffer(_gpu.SliceElements(0, 65536), _readback.SliceElements(0, 65536));
        }

        _commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _readback.SliceElements(0, _readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        if (_timer.Supported)
        {
            _timer.End(_commands);
        }

        _commands.Finish();
        EncodeMs = Ms(begin);
        _submitted = Stopwatch.GetTimestamp();
        _unknown = true;
        _submission = _device.Queue.Submit([_commands]);
        SubmitMs = Ms(_submitted);
        _initialized = true;
    }

    internal async Task CompleteAsync()
    {
        if (_submission is null)
        {
            return;
        }

        await _submission.WaitAsync();
        if (_submission.Status != SubmissionStatus.Completed)
        {
            throw new InvalidOperationException("Transfer fence did not complete.");
        }

        CompletionMs = Ms(_submitted);
        GpuMs = _timer.Supported ? _timer.ReadMilliseconds() : null;
        _unknown = false;
        _commands!.Dispose();
        _commands = null;
        _submission.Dispose();
        _submission = null;
    }

    internal async Task<byte[]> ReadAsync()
    {
        if (Pending || _unknown)
        {
            throw new InvalidOperationException("Readback before completed fence.");
        }

        byte[] raw = new byte[checked((int)_readback.Count)];
        await _readback.MapAsync();
        _readback.CopyTo(raw);
        _readback.Unmap();
        if (!_textureMode)
        {
            return raw;
        }

        byte[] packed = new byte[65536];
        for (int y = 0; y < 128; y++)
        {
            raw.AsSpan(y * checked((int)Pitch), 512).CopyTo(packed.AsSpan(y * 512, 512));
        }

        return packed;
    }

    private static uint Lcm(uint a, uint b)
    {
        uint x = a, y = b;
        while (y != 0)
        {
            (x, y) = (y, x % y);
        }

        return checked(a / x * b);
    }

    private static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
