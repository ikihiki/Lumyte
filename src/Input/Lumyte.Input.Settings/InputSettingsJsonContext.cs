using System.Text.Json.Serialization;

namespace Lumyte.Input.Settings;

/// <summary>Defines input settings json context.</summary>
[JsonSerializable(typeof(InputProcessingSettings))]
[JsonSerializable(typeof(InputActionSettings))]
public partial class InputSettingsJsonContext : JsonSerializerContext
{
}
