namespace Lumyte.Diagnostics.IntegrationTests;

internal sealed class SettingsDiagnosticModel
{
    public double Volume { get; set; } = 0.5;

    public bool Enabled { get; set; } = true;

    public long Count { get; set; } = 9007199254740993L;

    public string Device { get; set; } = "default";

    public string Secret { get; set; } = "not-for-diagnostics";
}
