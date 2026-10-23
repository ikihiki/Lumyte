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
        Handle = WebAudioInterop.CreateClip(device.Handle, format.SampleRate, format.Channels, System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples).ToArray());
    }

    public AudioFormat Format { get; }

    public long FrameCount { get; }

    internal WebAudioDevice Device { get; }

    internal JSObject Handle { get; }

    internal int References { get; set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Validate();
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
