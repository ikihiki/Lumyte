using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Settings.Tests;

/// <summary>Checks persistence-specific JSON metadata without modifying the caller's serializer.</summary>
public sealed class PersistenceJsonTests
{
    /// <summary>Missing keys use configured defaults, while explicitly saved defaults survive a restart.</summary>
    /// <param name="generated">Whether source-generated metadata supplies the JSON contract.</param>
    /// <returns>The asynchronous test operation.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OmissionOptionsCannotDropExplicitDefaultsAsync(bool generated)
    {
        JsonTypeInfo<Values> metadata = Metadata(generated);
        var store = new Store { Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"sample\":{\"schemaVersion\":1,\"values\":{}}}}") };
        using ServiceProvider provider = await ProviderAsync(store, metadata);
        IEditableOptions<Values> settings = provider.GetRequiredService<IEditableOptions<Values>>();
        Assert.Equal(7, settings.Current.Value.Count);
        Assert.Equal("default", settings.Current.Value.Label);
        SettingsEdit<Values> edit = settings.BeginEdit();
        edit.Value.Count = 0;
        edit.Value.AttributedCount = 0;
        edit.Value.Label = null;
        edit.Value.Child.Count = 0;
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);
        var saved = (JsonObject)JsonNode.Parse(store.Data!)!["sections"]!["sample"]!["values"]!;
        Assert.Equal(0, saved["count"]!.GetValue<int>());
        Assert.Equal(0, saved["attributedCount"]!.GetValue<int>());
        Assert.True(saved.ContainsKey("label"));
        Assert.Null(saved["label"]);
        Assert.Equal(0, saved["child"]!["count"]!.GetValue<int>());
        using ServiceProvider restarted = await ProviderAsync(store, metadata);
        IEditableOptions<Values> loaded = restarted.GetRequiredService<IEditableOptions<Values>>();
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.LoadResult.Status);
        Assert.Equal(0, loaded.Current.Value.Count);
        Assert.Equal(0, loaded.Current.Value.AttributedCount);
        Assert.Null(loaded.Current.Value.Label);
        Assert.Equal(0, loaded.Current.Value.Child.Count);
        Assert.DoesNotContain("\"count\"", JsonSerializer.Serialize(new Values { Count = 0, AttributedCount = 0, Label = null, Child = new NestedValues { Count = 0 } }, metadata));
    }

    /// <summary>Strict caller metadata does not retain or reject unknown persisted properties.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task UnknownPropertiesAreDiscardedInsideCollectionsAsync()
    {
        var store = new Store
        {
            Data = Encoding.UTF8.GetBytes("""
                {"documentVersion":1,"sections":{"sample":{"schemaVersion":1,"values":{
                  "unknown":true,"child":{"count":2,"unknown":true},
                  "items":[{"count":3,"unknown":true}],"map":{"entry":{"count":4,"unknown":true}}
                }}}}
                """),
        };
        using ServiceProvider provider = await ProviderAsync(store, PersistenceJsonContext.Default.Values);
        IEditableOptions<Values> settings = provider.GetRequiredService<IEditableOptions<Values>>();
        Assert.Equal(SettingsLoadStatus.Loaded, settings.LoadResult.Status);
        Assert.Equal(2, settings.Current.Value.Child.Count);
        Assert.Equal(3, settings.Current.Value.Items[0].Count);
        Assert.Equal(4, settings.Current.Value.Map["entry"].Count);
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(settings.BeginEdit())).Status);
        Assert.DoesNotContain("unknown", Encoding.UTF8.GetString(store.Data!));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"unknown\":true}", PersistenceJsonContext.Default.Values));
    }

    /// <summary>Root contract modifications remain in force when persistence normalizes omission settings.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task CustomizedRootPropertyNamesArePreservedAsync()
    {
        JsonTypeInfo<Values> metadata = Metadata(generated: false);
        metadata.Properties.Single(property => property.Name == "count").Name = "renamedCount";
        var store = new Store();
        using ServiceProvider provider = await ProviderAsync(store, metadata);
        IEditableOptions<Values> settings = provider.GetRequiredService<IEditableOptions<Values>>();
        SettingsEdit<Values> edit = settings.BeginEdit();
        edit.Value.Count = 12;
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);
        Assert.Contains("\"renamedCount\":12", Encoding.UTF8.GetString(store.Data!));
        using ServiceProvider restarted = await ProviderAsync(store, metadata);
        Assert.Equal(12, restarted.GetRequiredService<IEditableOptions<Values>>().Current.Value.Count);
    }

    private static JsonTypeInfo<Values> Metadata(bool generated)
    {
        if (generated)
        {
            return PersistenceJsonContext.Default.Values;
        }

        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        return (JsonTypeInfo<Values>)options.GetTypeInfo(typeof(Values));
    }

    private static async Task<ServiceProvider> ProviderAsync(Store store, JsonTypeInfo<Values> metadata)
    {
        PersistedSettingsSource source = await PersistedSettingsSource.LoadAsync(store);
        new ConfigurationManager().AddPersistedSettings(source);
        var services = new ServiceCollection();
        services.AddSettings(source);
        services.AddPersistedOptions<Values>("sample").UseJsonTypeInfo(metadata);
        return services.BuildServiceProvider();
    }

    /// <summary>Settings exercising omission and strict read options.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class Values
    {
        /// <summary>Gets or sets an implicitly omitted value.</summary>
        public int Count { get; set; } = 7;

        /// <summary>Gets or sets an explicitly omitted value.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int AttributedCount { get; set; } = 8;

        /// <summary>Gets or sets an explicitly omitted null.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Label { get; set; } = "default";

        /// <summary>Gets or sets a nested settings object.</summary>
        public NestedValues Child { get; set; } = new();

        /// <summary>Gets or sets nested objects in a list.</summary>
        public List<NestedValues> Items { get; set; } = [];

        /// <summary>Gets or sets nested objects in a dictionary.</summary>
        public Dictionary<string, NestedValues> Map { get; set; } = [];
    }

    /// <summary>A nested contract which ordinarily rejects unknown properties.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class NestedValues
    {
        /// <summary>Gets or sets an explicitly omitted nested value.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Count { get; set; } = 9;
    }

    private sealed class Store : ISettingsStore
    {
        public byte[]? Data { get; set; }

        public ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Data);

        public ValueTask WriteAtomicallyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            Data = data.ToArray();
            return ValueTask.CompletedTask;
        }
    }
}
