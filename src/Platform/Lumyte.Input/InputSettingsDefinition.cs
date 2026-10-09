using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Lumyte.Settings;

namespace Lumyte.Input;

internal sealed class InputSettingsDefinition : ISettingsDefinition<InputSettings>
{
    public int SchemaVersion => 1;

    public JsonTypeInfo<InputSettings> JsonTypeInfo => InputSettingsJsonContext.Default.InputSettings;

    public InputSettings DeepClone(InputSettings value) => new()
    {
        Bindings = value.Bindings?.ToDictionary(context => context.Key, context => context.Value?.ToDictionary(action => action.Key, action => action.Value?.ToArray()!)!)!,
        LeftStick = Copy(value.LeftStick),
        RightStick = Copy(value.RightStick),
        LeftTrigger = Copy(value.LeftTrigger),
        RightTrigger = Copy(value.RightTrigger),
    };

    public JsonObject Upgrade(JsonObject values, int sourceVersion)
    {
        if (sourceVersion != 1)
        {
            throw new NotSupportedException("Unsupported Input settings version.");
        }

        return values;
    }

    private static DeadZoneSettings Copy(DeadZoneSettings? value) => value is null ? null! : new() { Inner = value.Inner, Outer = value.Outer };
}
