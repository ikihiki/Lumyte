using System.Buffers;
using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.OpenAL;

internal sealed class OpenALClip : IAudioClip
{
    private bool _disposed;

    internal OpenALClip(OpenALDevice device, AudioFormat format, ReadOnlySpan<float> samples)
    {
        Device = device;
        Format = format;
        FrameCount = samples.Length / format.Channels;
        Handle = device.Api.GenBuffer();
        device.CheckError();
        try
        {
            short[] pcm = ArrayPool<short>.Shared.Rent(samples.Length);
            try
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    pcm[i] = (short)MathF.Round(Math.Clamp(samples[i], -1, 1) * 32767);
                }

                unsafe
                {
                    fixed (short* data = pcm)
                    {
                        device.Api.BufferData(Handle, format.Channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16, data, checked(samples.Length * sizeof(short)), format.SampleRate);
                    }
                }

                device.CheckError();
            }
            finally
            {
                ArrayPool<short>.Shared.Return(pcm);
            }
        }
        catch
        {
            device.Api.DeleteBuffer(Handle);
            device.CheckError();
            throw;
        }
    }

    public AudioFormat Format { get; }

    public long FrameCount { get; }

    IAudioDevice IAudioClip.Device => Device;

    internal OpenALDevice Device { get; }

    internal uint Handle { get; }

    internal int References { get; set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (References != 0)
        {
            throw new InvalidOperationException("Clip is borrowed by a live voice.");
        }

        if (Device.PrepareCleanup())
        {
            Device.Api.DeleteBuffer(Handle);
            Device.CheckError();
        }

        _disposed = true;
        Device.Remove(this);
    }

    internal void Validate()
    {
        Device.Validate();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
