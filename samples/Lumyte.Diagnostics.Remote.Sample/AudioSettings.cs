namespace Lumyte.Diagnostics.Remote.Sample;

internal sealed class AudioSettings
{
    public double Volume { get; set; } = 0.5;

    public bool Muted { get; set; }

    public string Device { get; set; } = "default";
}
