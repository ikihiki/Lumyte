using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Settings.Tests;

/// <summary>Checks that required serializer members still receive missing-key settings defaults.</summary>
public sealed class RequiredPropertiesTests
{
    /// <summary>Nested required keys are supplemented before saved sibling values are applied.</summary>
    /// <param name="generated">Whether source-generated metadata supplies the JSON contract.</param>
    /// <returns>The asynchronous test operation.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingNestedRequiredKeysUseConfiguredDefaultsAsync(bool generated)
    {
        JsonTypeInfo<NestedSettings> metadata = generated ? RequiredPropertiesJsonContext.Default.NestedSettings : Metadata<NestedSettings>();
        var store = new Store { Data = Document("{\"child\":{\"maximum\":19}}") };
        using ServiceProvider provider = await ProviderAsync(store, metadata, services => services.Configure<NestedSettings>(value => value.Child.Minimum = 15));
        IEditableOptions<NestedSettings> settings = provider.GetRequiredService<IEditableOptions<NestedSettings>>();
        Assert.Equal(SettingsLoadStatus.Loaded, settings.LoadResult.Status);
        Assert.Equal(15, settings.Current.Value.Child.Minimum);
        Assert.Equal(19, settings.Current.Value.Child.Maximum);
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(settings.BeginEdit())).Status);

        using ServiceProvider restarted = await ProviderAsync(store, metadata, services => services.Configure<NestedSettings>(value => value.Child.Minimum = 25));
        IEditableOptions<NestedSettings> loaded = restarted.GetRequiredService<IEditableOptions<NestedSettings>>();
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.LoadResult.Status);
        Assert.Equal(15, loaded.Current.Value.Child.Minimum);
        Assert.Equal(19, loaded.Current.Value.Child.Maximum);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"child\":{\"maximum\":19}}", metadata));
    }

    /// <summary>Required root keys do not serialize unnormalized defaults before saved values apply.</summary>
    /// <param name="generated">Whether source-generated metadata supplies the JSON contract.</param>
    /// <returns>The asynchronous test operation.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRootRequiredKeysAllowNormalizationAndRoundTripAsync(bool generated)
    {
        JsonTypeInfo<RequiredSettings> metadata = generated ? RequiredPropertiesJsonContext.Default.RequiredSettings : Metadata<RequiredSettings>();
        var store = new Store { Data = Document("{\"volume\":0.7}") };
        using ServiceProvider provider = await ProviderAsync(store, metadata, ConfigureRequired);
        IEditableOptions<RequiredSettings> settings = provider.GetRequiredService<IEditableOptions<RequiredSettings>>();
        Assert.Equal(SettingsLoadStatus.Loaded, settings.LoadResult.Status);
        Assert.Equal(7, settings.Current.Value.Count);
        Assert.Equal(0.7, settings.Current.Value.Volume);
        SettingsEdit<RequiredSettings> edit = settings.BeginEdit();
        edit.Value.Volume = 0.8;
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);

        using ServiceProvider restarted = await ProviderAsync(store, metadata, ConfigureRequired);
        IEditableOptions<RequiredSettings> loaded = restarted.GetRequiredService<IEditableOptions<RequiredSettings>>();
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.LoadResult.Status);
        Assert.Equal(7, loaded.Current.Value.Count);
        Assert.Equal(0.8, loaded.Current.Value.Volume);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"volume\":0.7}", metadata));
    }

    private static void ConfigureRequired(IServiceCollection services) => services.AddOptions<RequiredSettings>()
        .Configure(value => value.Volume = double.NaN)
        .PostConfigure(value => value.Volume = double.IsFinite(value.Volume) ? value.Volume : 0.5)
        .Validate(value => double.IsFinite(value.Volume), "Volume must be finite.");

    private static byte[] Document(string values) => Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"sample\":{\"schemaVersion\":1,\"values\":" + values + "}}}");

    private static JsonTypeInfo<T> Metadata<T>()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        return (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
    }

    private static async Task<ServiceProvider> ProviderAsync<T>(Store store, JsonTypeInfo<T> metadata, Action<IServiceCollection> configure)
        where T : class, new()
    {
        PersistedSettingsSource source = await PersistedSettingsSource.LoadAsync(store);
        new ConfigurationManager().AddPersistedSettings(source);
        var services = new ServiceCollection();
        services.AddSettings(source);
        services.AddPersistedOptions<T>("sample").UseJsonTypeInfo(metadata);
        configure(services);
        return services.BuildServiceProvider();
    }

    /// <summary>A root whose nested contract declares required JSON members.</summary>
    public sealed class NestedSettings
    {
        /// <summary>Gets or sets the nested settings.</summary>
        public RequiredRange Child { get; set; } = new();
    }

    /// <summary>A nested required member which can be added in a later settings version.</summary>
    public sealed class RequiredRange
    {
        /// <summary>Gets or sets the required bound.</summary>
        [JsonRequired]
        public int Minimum { get; set; } = 5;

        /// <summary>Gets or sets the independently persisted bound.</summary>
        public int Maximum { get; set; } = 6;
    }

    /// <summary>A required root member alongside a value normalized through Options.</summary>
    public sealed class RequiredSettings
    {
        /// <summary>Gets or sets the required value.</summary>
        [JsonRequired]
        public int Count { get; set; } = 7;

        /// <summary>Gets or sets the value which may require normalization.</summary>
        public double Volume { get; set; }
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
