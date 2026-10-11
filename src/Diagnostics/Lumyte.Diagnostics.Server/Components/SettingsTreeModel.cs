using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumyte.Diagnostics.Server.Components;

/// <summary>A recursively editable JSON settings node used by the server UI.</summary>
public sealed class SettingsTreeModel
{
    private SettingsTreeModel(string name, string path, JsonValueKind kind, string value, List<SettingsTreeModel> children)
    {
        Name = name;
        Path = path;
        Kind = kind;
        Value = value;
        Children = children;
    }

    /// <summary>Gets the property name or array index shown in the tree.</summary>
    public string Name { get; }

    /// <summary>Gets the JSON Pointer path.</summary>
    public string Path { get; }

    /// <summary>Gets the JSON value kind.</summary>
    public JsonValueKind Kind { get; }

    /// <summary>Gets or sets the editable scalar text.</summary>
    public string Value { get; set; }

    /// <summary>Gets the child nodes.</summary>
    public List<SettingsTreeModel> Children { get; }

    /// <summary>Gets a value indicating whether this node contains children.</summary>
    public bool IsContainer => Kind is JsonValueKind.Object or JsonValueKind.Array;

    /// <summary>Parses one settings document.</summary>
    /// <param name="name">The root display name.</param>
    /// <param name="json">The JSON document.</param>
    /// <returns>The editable tree.</returns>
    public static SettingsTreeModel Parse(string name, string json)
    {
        JsonNode node = JsonNode.Parse(json) ?? throw new JsonException("The settings document is empty.");
        return Create(name, "/", node);
    }

    /// <summary>Writes the complete edited tree as JSON.</summary>
    /// <returns>The JSON document.</returns>
    public string ToJson() => ToNode()?.ToJsonString() ?? "null";

    private static SettingsTreeModel Create(string name, string path, JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            return new(name, path, JsonValueKind.Object, string.Empty, obj.Select(item => Create(item.Key, ChildPath(path, item.Key), item.Value)).ToList());
        }

        if (node is JsonArray array)
        {
            return new(name, path, JsonValueKind.Array, string.Empty, array.Select((item, index) => Create($"[{index}]", ChildPath(path, index.ToString(CultureInfo.InvariantCulture)), item)).ToList());
        }

        JsonValueKind kind = node?.GetValueKind() ?? JsonValueKind.Null;
        string value = kind switch
        {
            JsonValueKind.String => node!.GetValue<string>(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => node!.ToJsonString(),
        };
        return new(name, path, kind, value, []);
    }

    private static string ChildPath(string path, string child) => path == "/" ? "/" + Escape(child) : path + "/" + Escape(child);

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private JsonNode? ToNode()
    {
        if (Kind == JsonValueKind.Object)
        {
            var result = new JsonObject();
            foreach (SettingsTreeModel child in Children)
            {
                result.Add(child.Name, child.ToNode());
            }

            return result;
        }

        if (Kind == JsonValueKind.Array)
        {
            var result = new JsonArray();
            foreach (SettingsTreeModel child in Children)
            {
                result.Add(child.ToNode());
            }

            return result;
        }

        return Kind switch
        {
            JsonValueKind.String => JsonValue.Create(Value),
            JsonValueKind.Number => JsonNode.Parse(Value),
            JsonValueKind.True or JsonValueKind.False => JsonValue.Create(bool.Parse(Value)),
            JsonValueKind.Null => null,
            _ => throw new JsonException($"Unsupported JSON value at {Path}."),
        };
    }
}
