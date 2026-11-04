using System.Text.Json;
using Lumyte.UI.Navigation;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application.Navigation.Diagnostics;

/// <summary>Parses the closed version 1 observation schema without reflection or UI access.</summary>
public static class UiNavigationStateSchema
{
    /// <summary>Reads detached committed identities, rejecting unknown, duplicate or incorrectly typed members.</summary>
    /// <param name="json">A schema version 1 state result, at most 32 KiB.</param>
    /// <returns>The immutable window observations.</returns>
    /// <exception cref="JsonException">The observation does not match the closed schema.</exception>
    public static IReadOnlyList<UiNavigationWindowSnapshot> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 32768)
        {
            throw new JsonException("Navigation metadata exceeds its bound.");
        }

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Object(root, "schemaVersion", "windows");
        if (Number(root.GetProperty("schemaVersion")) != 1 || root.GetProperty("windows").ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Unknown navigation schema or invalid windows list.");
        }

        var identities = new HashSet<Guid>();
        var windows = new List<UiNavigationWindowSnapshot>();
        foreach (JsonElement window in root.GetProperty("windows").EnumerateArray())
        {
            Object(window, "windowContextId", "windowManagerId", "windowValue", "revision", "current");
            Guid id = Identity(window.GetProperty("windowContextId"));
            ulong value = Number(window.GetProperty("windowValue"));
            if (!identities.Add(id) || value == 0)
            {
                throw new JsonException("Duplicate window instance or default logical identity.");
            }

            JsonElement current = window.GetProperty("current");
            UiNavigationCommittedView? publication = null;
            if (current.ValueKind != JsonValueKind.Null)
            {
                Object(current, "navigatorId", "entryId", "activationId", "route", "surfaceId", "targetId");
                JsonElement routeValue = current.GetProperty("route");
                if (routeValue.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException("The route must be a canonical string.");
                }

                string route = routeValue.GetString()!;
                try
                {
                    _ = new UiPageKey<UiNoArgs>(route);
                }
                catch (ArgumentException error)
                {
                    throw new JsonException("Invalid static route.", error);
                }

                JsonElement target = current.GetProperty("targetId");
                publication = new(Identity(current.GetProperty("navigatorId")), Identity(current.GetProperty("entryId")), Identity(current.GetProperty("activationId")), route, Identity(current.GetProperty("surfaceId")), target.ValueKind == JsonValueKind.Null ? null : Identity(target));
            }

            windows.Add(new(id, new UiWindowId(Identity(window.GetProperty("windowManagerId")), value), Number(window.GetProperty("revision")), publication));
        }

        return windows.AsReadOnly();
    }

    private static void Object(JsonElement value, params string[] members)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected a metadata object.");
        }

        var remaining = new HashSet<string>(members, StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!remaining.Remove(property.Name))
            {
                throw new JsonException("Unknown or duplicate metadata member.");
            }
        }

        if (remaining.Count != 0)
        {
            throw new JsonException("Missing metadata member.");
        }
    }

    private static Guid Identity(JsonElement value) => value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "D", out Guid id) && id != Guid.Empty ? id : throw new JsonException("Expected a nondefault Guid in D format.");

    private static ulong Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong number) ? number : throw new JsonException("Expected an unsigned JSON integer.");
}
