using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumyte.DevTools.Server;

public sealed class DevToolsRemoteJsonSession : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DevToolsHostRegistry registry;
    private readonly Func<string, CancellationToken, ValueTask> send;
    private readonly Dictionary<string, Action<DevToolsRemoteEvent>> subscriptions = [];
    private int nextSubscriptionId;

    public DevToolsRemoteJsonSession(DevToolsHostRegistry registry, Func<string, CancellationToken, ValueTask> send)
    {
        this.registry = registry;
        this.send = send;
    }

    public async ValueTask<string> ProcessAsync(string json, CancellationToken cancellationToken = default)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException exception) { return Error(null, "invalid_json", exception.Message); }
        using (document)
        {
            JsonElement root = document.RootElement;
            JsonNode? id = root.TryGetProperty("id", out JsonElement idValue) ? JsonNode.Parse(idValue.GetRawText()) : null;
            try
            {
                string method = Required(root, "method");
                JsonNode? result = method switch
                {
                    "negotiate" => Negotiate(root),
                    "hosts" => Hosts(),
                    "domains" => Domains(ResolveHost(root)),
                    "invoke" => await InvokeAsync(root, ResolveHost(root), cancellationToken).ConfigureAwait(false),
                    "subscribe" => Subscribe(root, ResolveHost(root)),
                    "unsubscribe" => Unsubscribe(root),
                    _ => throw new RemoteProtocolException("method_not_found", $"The method '{method}' is not supported."),
                };
                return new JsonObject { ["id"] = id, ["result"] = result }.ToJsonString(JsonOptions);
            }
            catch (RemoteProtocolException exception) { return Error(id, exception.Code, exception.Message); }
            catch (KeyNotFoundException exception) { return Error(id, "host_not_found", exception.Message); }
            catch (JsonException exception) { return Error(id, "invalid_params", exception.Message); }
            catch (Exception exception) when (exception is not OperationCanceledException) { return Error(id, "remote_error", exception.Message); }
        }
    }

    public void Dispose()
    {
        foreach (Action<DevToolsRemoteEvent> handler in subscriptions.Values)
        {
            registry.EventReceived -= handler;
        }

        subscriptions.Clear();
    }

    private static JsonObject Negotiate(JsonElement root)
    {
        string requested = Required(root, "protocolVersion");
        if (requested != "1.0")
        {
            throw new RemoteProtocolException("incompatible_protocol", $"Protocol '{requested}' is not supported. Supported versions: 1.0.");
        }

        return new JsonObject
        {
            ["protocolVersion"] = "1.0",
            ["supportedVersions"] = new JsonArray("1.0"),
            ["capabilities"] = new JsonArray("subscriptions", "operations", "diagnostics-v1"),
            ["heartbeatIntervalMilliseconds"] = 5000,
        };
    }
    private JsonArray Hosts() => new(registry.Hosts.Select(host => (JsonNode)new JsonObject
    {
        ["hostId"] = host.HostId, ["displayName"] = host.DisplayName, ["connected"] = host.IsConnected,
    }).ToArray());

    private JsonArray Domains(string hostId) => new(registry.GetHost(hostId).Domains.Select(domain => (JsonNode)new JsonObject
    {
        ["name"] = domain.Name,
        ["features"] = new JsonArray(domain.Features.Select(feature => (JsonNode)new JsonObject
        {
            ["name"] = feature.Name, ["kind"] = feature.Kind, ["requestType"] = feature.RequestType, ["responseType"] = feature.ResponseType,
        }).ToArray()),
    }).ToArray());

    private async ValueTask<JsonNode?> InvokeAsync(JsonElement root, string hostId, CancellationToken cancellationToken)
    {
        JsonElement parameters = root.TryGetProperty("params", out JsonElement value) ? value.Clone() : JsonSerializer.SerializeToElement(new { });
        JsonElement result = await registry.InvokeAsync(hostId, Required(root, "domain"), Required(root, "feature"), Required(root, "kind"), parameters, cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(result.GetRawText());
    }

    private JsonObject Subscribe(JsonElement root, string hostId)
    {
        string domain = Required(root, "domain");
        string feature = Required(root, "feature");
        string id = Interlocked.Increment(ref nextSubscriptionId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Action<DevToolsRemoteEvent> handler = value =>
        {
            if (value.HostId == hostId && value.Domain == domain && value.Feature == feature)
            {
                string message = new JsonObject { ["event"] = new JsonObject
                {
                    ["subscriptionId"] = id, ["hostId"] = hostId, ["domain"] = domain, ["feature"] = feature,
                    ["params"] = JsonNode.Parse(value.Params.GetRawText()),
                }}.ToJsonString(JsonOptions);
                _ = send(message, CancellationToken.None);
            }
        };
        registry.EventReceived += handler;
        subscriptions.Add(id, handler);
        return new JsonObject { ["subscriptionId"] = id };
    }

    private JsonObject Unsubscribe(JsonElement root)
    {
        string id = Required(root, "subscriptionId");
        if (!subscriptions.Remove(id, out Action<DevToolsRemoteEvent>? handler))
        {
            throw new RemoteProtocolException("subscription_not_found", $"Subscription '{id}' was not found.");
        }

        registry.EventReceived -= handler;
        return new JsonObject { ["unsubscribed"] = true };
    }

    private string ResolveHost(JsonElement root)
    {
        if (root.TryGetProperty("hostId", out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()!;
        }

        DevToolsRemoteHost[] hosts = registry.Hosts.ToArray();
        return hosts.Length == 1 ? hosts[0].HostId : throw new RemoteProtocolException("host_required", "'hostId' is required unless exactly one host is connected.");
    }

    private static string Required(JsonElement root, string name) => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new RemoteProtocolException("invalid_request", $"'{name}' must be a string.");

    private static string Error(JsonNode? id, string code, string message) => new JsonObject
    {
        ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message, ["retryable"] = code is "host_not_found" or "remote_error" },
    }.ToJsonString(JsonOptions);

    private sealed class RemoteProtocolException(string code, string message) : Exception(message) { public string Code { get; } = code; }
}
