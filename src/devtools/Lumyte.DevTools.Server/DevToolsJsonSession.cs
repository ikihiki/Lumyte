using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumyte.DevTools.Server;

public sealed class DevToolsJsonSession : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DevToolsHub hub;
    private readonly Func<string, CancellationToken, ValueTask> send;
    private readonly Dictionary<string, IDisposable> subscriptions = [];
    private int nextSubscriptionId;
    private int disposed;

    public DevToolsJsonSession(DevToolsHub hub, Func<string, CancellationToken, ValueTask> send)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(send);
        this.hub = hub;
        this.send = send;
    }

    public async ValueTask<string> ProcessAsync(string json, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return CreateError(null, "invalid_json", exception.Message).ToJsonString(JsonOptions);
        }

        using (document)
        {
            JsonNode? id = null;
            try
            {
                JsonElement root = document.RootElement;
                id = GetId(root);
            string method = GetRequiredString(root, "method");
            JsonNode? result = method switch
            {
                "domains" => GetDomains(),
                "invoke" => await InvokeAsync(root, cancellationToken).ConfigureAwait(false),
                "subscribe" => Subscribe(root),
                "unsubscribe" => Unsubscribe(root),
                _ => throw new ProtocolException("method_not_found", $"The method '{method}' is not supported."),
            };
            return CreateResponse(id, result).ToJsonString(JsonOptions);
        }
            catch (JsonException exception)
            {
                return CreateError(id, "invalid_params", exception.Message).ToJsonString(JsonOptions);
            }
            catch (ProtocolException exception)
        {
            return CreateError(id, exception.Code, exception.Message).ToJsonString(JsonOptions);
        }
        catch (DevToolsFeatureNotRegisteredException exception)
        {
            return CreateError(id, "feature_not_found", exception.Message).ToJsonString(JsonOptions);
        }
        catch (DevToolsContractMismatchException exception)
        {
            return CreateError(id, "contract_mismatch", exception.Message).ToJsonString(JsonOptions);
        }
        catch (DevToolsException exception)
        {
            return CreateError(id, "devtools_error", exception.Message).ToJsonString(JsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
            catch (Exception exception)
            {
                return CreateError(id, "invalid_params", exception.Message).ToJsonString(JsonOptions);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        foreach (IDisposable subscription in subscriptions.Values)
        {
            subscription.Dispose();
        }

        subscriptions.Clear();
    }

    private JsonArray GetDomains()
    {
        JsonArray domains = [];
        foreach (DevToolsDomain domain in hub.Domains)
        {
            JsonArray features = [];
            foreach (DevToolsFeature feature in hub.GetFeatures(domain))
            {
                features.Add(new JsonObject
                {
                    ["name"] = feature.Name,
                    ["kind"] = feature.Kind.ToString().ToLowerInvariant(),
                    ["requestType"] = feature.RequestType.FullName,
                    ["responseType"] = feature.ResponseType?.FullName,
                });
            }

            domains.Add(new JsonObject { ["name"] = domain.Name, ["features"] = features });
        }

        return domains;
    }

    private async ValueTask<JsonNode?> InvokeAsync(JsonElement root, CancellationToken cancellationToken)
    {
        DevToolsDomain domain = new(GetRequiredString(root, "domain"));
        string featureName = GetRequiredString(root, "feature");
        DevToolsFeatureKind kind = ParseInvocableKind(GetRequiredString(root, "kind"));
        DevToolsFeature feature = hub.GetFeatures(domain).FirstOrDefault(
            candidate => candidate.Kind == kind && StringComparer.Ordinal.Equals(candidate.Name, featureName))
            ?? throw new ProtocolException("feature_not_found", $"The {kind.ToString().ToLowerInvariant()} '{domain.Name}/{featureName}' is not registered.");
        object? request = root.TryGetProperty("params", out JsonElement parameters)
            ? parameters.Deserialize(feature.RequestType, JsonOptions)
            : JsonSerializer.Deserialize("{}", feature.RequestType, JsonOptions);
        object? result = await hub.InvokeAsync(domain, kind, featureName, request, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToNode(result, feature.ResponseType!, JsonOptions);
    }

    private JsonObject Subscribe(JsonElement root)
    {
        DevToolsDomain domain = new(GetRequiredString(root, "domain"));
        string feature = GetRequiredString(root, "feature");
        string subscriptionId = Interlocked.Increment(ref nextSubscriptionId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        IDisposable subscription = hub.Subscribe(
            domain,
            feature,
            (value, token) => send(CreateEvent(subscriptionId, domain.Name, feature, value), token));
        subscriptions.Add(subscriptionId, subscription);
        return new JsonObject { ["subscriptionId"] = subscriptionId };
    }

    private JsonObject Unsubscribe(JsonElement root)
    {
        string subscriptionId = GetRequiredString(root, "subscriptionId");
        if (!subscriptions.Remove(subscriptionId, out IDisposable? subscription))
        {
            throw new ProtocolException("subscription_not_found", $"The subscription '{subscriptionId}' does not exist.");
        }

        subscription.Dispose();
        return new JsonObject { ["unsubscribed"] = true };
    }

    private static string CreateEvent(string subscriptionId, string domain, string feature, object? value)
    {
        JsonObject envelope = new()
        {
            ["event"] = new JsonObject
            {
                ["subscriptionId"] = subscriptionId,
                ["domain"] = domain,
                ["feature"] = feature,
                ["params"] = JsonSerializer.SerializeToNode(value, JsonOptions),
            },
        };
        return envelope.ToJsonString(JsonOptions);
    }

    private static JsonNode? GetId(JsonElement root) => root.TryGetProperty("id", out JsonElement id)
        ? JsonNode.Parse(id.GetRawText())
        : null;

    private static string GetRequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ProtocolException("invalid_request", $"'{name}' must be a string.");
        }

        return value.GetString()!;
    }

    private static DevToolsFeatureKind ParseInvocableKind(string value) => value switch
    {
        "query" => DevToolsFeatureKind.Query,
        "command" => DevToolsFeatureKind.Command,
        _ => throw new ProtocolException("invalid_request", "'kind' must be 'query' or 'command'."),
    };

    private static JsonObject CreateResponse(JsonNode? id, JsonNode? result) => new()
    {
        ["id"] = id,
        ["result"] = result,
    };

    private static JsonObject CreateError(JsonNode? id, string code, string message) => new()
    {
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private sealed class ProtocolException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
