using Lumyte.Audio.Abstractions;

namespace Lumyte.Resources.Audio.Sample;

internal sealed class TestClip(TestDevice device, AudioFormat format, long frames) : IAudioClip
{
    public IAudioDevice Device => device;

    public AudioFormat Format => format;

    public long FrameCount => frames;

    internal int Borrowers { get; set; }

    public void Dispose()
    {
        device.Verify();
        if (Borrowers != 0)
        {
            throw new InvalidOperationException("A live voice borrows this clip.");
        }

        device.ReturnClip();
    }
}
