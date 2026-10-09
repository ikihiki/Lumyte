using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Lumyte.Settings;

/// <summary>A configuration source retaining the original JSON and borrowed storage medium.</summary>
public abstract class PersistedSettingsSource : IConfigurationSource
{
    private readonly object _gate = new();
    private bool _loaded;
    private JsonObject _raw = EmptyDocument();
    private SettingsLoadResult _result = new(SettingsLoadStatus.Defaults, []);
    private Provider? _provider;
    private IConfigurationBuilder? _builder;

    private protected PersistedSettingsSource(ISettingsStore store)
    {
        Store = store;
    }

    internal ISettingsStore Store { get; }

    internal bool IsLoaded
    {
        get
        {
            lock (_gate)
            {
                return _loaded;
            }
        }
    }

    internal SettingsLoadResult Result => _result;

    internal bool IsRegistered => _builder is not null;

    /// <summary>Loads an asynchronous medium before registering the configuration source.</summary>
    /// <param name="store">The borrowed storage medium.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A loaded source, including diagnostics on data or storage failures.</returns>
    public static async Task<PersistedSettingsSource> LoadAsync(ISettingsStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var source = new LoadedSource(store);
        try
        {
            source.Accept(await store.ReadAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (IOException error)
        {
            source.AcceptFailure(error);
        }

        return source;
    }

    /// <summary>Creates the synchronous configuration provider for one configuration root.</summary>
    /// <param name="builder">The owning configuration builder.</param>
    /// <returns>The provider.</returns>
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        lock (_gate)
        {
            if (_builder is not null && !ReferenceEquals(_builder, builder))
            {
                throw new InvalidOperationException("A settings source belongs to one configuration builder.");
            }

            _builder = builder;
            return _provider ??= new Provider(this);
        }
    }

    internal static JsonObject EmptyDocument() => new() { ["documentVersion"] = 1, ["sections"] = new JsonObject() };

    internal JsonObject CopyDocument()
    {
        lock (_gate)
        {
            return (JsonObject)_raw.DeepClone();
        }
    }

    internal void Publish(JsonObject document)
    {
        lock (_gate)
        {
            _raw = document;
            _provider?.Publish(document);
        }
    }

    private protected virtual byte[]? ReadSynchronously() => throw new InvalidOperationException("The asynchronous source must already be loaded.");

    private void EnsureLoaded()
    {
        lock (_gate)
        {
            if (!_loaded)
            {
                try
                {
                    Accept(ReadSynchronously());
                }
                catch (IOException error)
                {
                    AcceptFailure(error);
                }
            }

            _provider!.Publish(_raw);
        }
    }

    private void Accept(byte[]? bytes)
    {
        _loaded = true;
        if (bytes is null)
        {
            return;
        }

        try
        {
            JsonObject document = JsonNode.Parse(bytes) as JsonObject ?? throw new JsonException("Settings must be a JSON object.");
            if (document["documentVersion"] is not JsonValue version || !version.TryGetValue<int>(out int number))
            {
                throw new JsonException("documentVersion must be an integer.");
            }

            if (number != 1)
            {
                _result = new(SettingsLoadStatus.UnsupportedVersion, ["Unsupported documentVersion."]);
                return;
            }

            if (document["sections"] is not JsonObject || document.Any(pair => pair.Key is not ("documentVersion" or "sections")))
            {
                throw new JsonException("The settings document has an invalid envelope.");
            }

            _raw = document;
            _result = new(SettingsLoadStatus.Loaded, []);
        }
        catch (JsonException error)
        {
            _result = new(SettingsLoadStatus.InvalidData, [error.Message]);
        }
    }

    private void AcceptFailure(IOException error)
    {
        _loaded = true;
        _result = new(SettingsLoadStatus.StorageFailure, [error.Message]);
    }

    private sealed class LoadedSource(ISettingsStore store) : PersistedSettingsSource(store);

    private sealed class Provider(PersistedSettingsSource source) : ConfigurationProvider
    {
        public override void Load() => source.EnsureLoaded();

        public override void Set(string key, string? value) => throw new InvalidOperationException("Persist settings through IEditableOptions, not IConfiguration.Set.");

        internal void Publish(JsonObject document)
        {
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            Flatten(document, string.Empty, data);
            Data = data;
        }

        private static void Flatten(JsonNode? node, string path, Dictionary<string, string?> data)
        {
            if (node is JsonObject obj)
            {
                foreach ((string key, JsonNode? value) in obj)
                {
                    Flatten(value, path.Length == 0 ? key : $"{path}:{key}", data);
                }
            }
            else if (node is JsonArray array)
            {
                for (int index = 0; index < array.Count; index++)
                {
                    Flatten(array[index], $"{path}:{index}", data);
                }
            }
            else
            {
                data[path] = node is JsonValue value && value.TryGetValue<string>(out string? text) ? text : node?.ToJsonString();
            }
        }
    }
}
