using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Browser;

internal sealed class WebAudioClip : IAudioClip
{
    private bool _disposed;

    internal WebAudioClip(WebAudioDevice device, AudioFormat format, ReadOnlySpan<float> samples)
    {
        Device = device;
        Format = format;
        FrameCount = samples.Length / format.Channels;

        // The synchronous import only reads the borrowed bytes and copies them into its AudioBuffer.
        // MemoryView requires a writable Span even though this operation does not modify the input.
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(samples);
        Handle = WebAudioInterop.CreateClip(device.Handle, format.SampleRate, format.Channels, MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(bytes), bytes.Length));
    }

    public AudioFormat Format { get; }

    public long FrameCount { get; }

    IAudioDevice IAudioClip.Device => Device;

    internal WebAudioDevice Device { get; }

    internal JSObject Handle { get; }

    internal int References { get; set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Device.Validate(false);
        if (References != 0)
        {
            throw new InvalidOperationException("Clip is borrowed by a live voice.");
        }

        Handle.Dispose();
        _disposed = true;
        Device.Remove(this);
    }

    internal void Validate()
    {
        Device.Validate();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
