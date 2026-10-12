namespace Lumyte.Diagnostics.Remote.Sample;

internal sealed class AudioSettings
{
    public double Volume { get; set; } = 0.5;

    public bool Muted { get; set; }

    public string Device { get; set; } = "default";

    public OutputSettings Output { get; set; } = new();

    public List<AudioBusSettings> Buses { get; set; } =
    [
        new() { Name = "マスター", Gain = 1 },
        new() { Name = "効果音", Gain = 0.8 },
        new() { Name = "音楽", Gain = 0.65 },
    ];

    public Dictionary<string, string> Labels { get; set; } = new()
    {
        ["profile"] = "標準",
        ["locale"] = "日本語",
    };
}
