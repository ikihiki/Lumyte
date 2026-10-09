using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Lumyte.Settings.Tests;

internal sealed class OtherDefinition : ISettingsDefinition<OtherSettings>
{
    public int SchemaVersion => 2;

    public JsonTypeInfo<OtherSettings> JsonTypeInfo => TestJsonContext.Default.OtherSettings;

    public OtherSettings DeepClone(OtherSettings value) => new() { Number = value.Number };

    public JsonObject Upgrade(JsonObject values, int sourceVersion)
    {
        if (sourceVersion == 1 && values["number"] is not null)
        {
            values["number"] = values["number"]!.GetValue<int>() + 10;
        }

        return values;
    }
}
