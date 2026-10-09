using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Lumyte.Settings.Tests;

internal sealed class SampleDefinition : ISettingsDefinition<SampleSettings>
{
    public int SchemaVersion => 1;

    public JsonTypeInfo<SampleSettings> JsonTypeInfo => TestJsonContext.Default.SampleSettings;

    public SampleSettings DeepClone(SampleSettings value) => new()
    {
        Entries = value.Entries?.ToDictionary(group => group.Key, group => group.Value?.ToDictionary(entry => entry.Key, entry => entry.Value?.ToArray()!)!)!,
        PrimaryRange = Copy(value.PrimaryRange),
        SecondaryRange = Copy(value.SecondaryRange),
    };

    public JsonObject Upgrade(JsonObject values, int sourceVersion)
    {
        if (sourceVersion != 1)
        {
            throw new NotSupportedException("Unsupported sample settings version.");
        }

        return values;
    }

    private static SampleRange Copy(SampleRange? value) => value is null ? null! : new() { Minimum = value.Minimum, Maximum = value.Maximum };
}
