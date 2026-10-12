namespace Lumyte.Diagnostics.Remote.Sample;

internal sealed class OutputSettings
{
    public string Backend { get; set; } = "WASAPI";

    public long SampleRate { get; set; } = 48000;

    public bool SpatialAudio { get; set; } = true;
}
