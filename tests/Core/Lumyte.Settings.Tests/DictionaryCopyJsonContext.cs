using System.Text.Json.Serialization;

namespace Lumyte.Settings.Tests;

[JsonSerializable(typeof(DictionaryCopySettings))]
internal partial class DictionaryCopyJsonContext : JsonSerializerContext;
