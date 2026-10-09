using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Lumyte.Diagnostics.Sample;
using Lumyte.Diagnostics.Server;
using Lumyte.Diagnostics.Settings;
using Lumyte.Diagnostics.Transport;
using Lumyte.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks opt-in settings fields, asynchronous saves and real transport round trips.</summary>
public sealed class SettingsDiagnosticsTests
{
    private static readonly Guid _session = Guid.NewGuid();
    private static readonly HashSet<DiagnosticPermission> _permissions = [DiagnosticPermission.Observe, DiagnosticPermission.Edit];

    /// <summary>Checks read-only fields and secrets stay out of save schemas.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task PublishesOnlyExplicitFieldsAsync()
    {
        var store = new Store();
        await using ServiceProvider provider = await CreateAsync(store);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var set = new DiagnosticOperationSet(scope.ServiceProvider.GetRequiredService<SettingsDiagnostics<SettingsDiagnosticModel>>());
        OperationDescriptor read = Assert.Single(set.Catalog, operation => operation.Id == "read");
        Assert.DoesNotContain(read.Results, field => field.Id == "secret");
        Assert.Contains(read.Results, field => field.Id == "device");
        Assert.DoesNotContain(Assert.Single(set.Catalog, operation => operation.Id == "save").Arguments, field => field.Id == "device");
        DiagnosticOperationResult snapshot = Invoke(set, "read");
        Assert.IsAssignableFrom<DiagnosticOutputValues>(snapshot.Values);
        Assert.Equal(9007199254740993L, snapshot.Values!["count"].Int64);
        Assert.Equal("Defaults", snapshot.Values["load-status"].String);
        Assert.Equal(0, snapshot.Revision);
        Assert.Equal("forbidden", Invoke(set, "read", permissions: []).Code);
        Assert.Equal("revision-required", Invoke(set, "save", Edit()).Code);
        Assert.Equal("forbidden", Invoke(set, "save", Edit(), 0, [DiagnosticPermission.Observe]).Code);
        Assert.Equal("conflict", Invoke(set, "save", Edit(), 99).Status);
        Assert.Null(store.Bytes);
    }

    /// <summary>Checks pending saves do not block, bound jobs, preserve hidden fields and cancel before commit.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task PendingSavesAreBoundedAndCancellableAsync()
    {
        var store = new Store { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using ServiceProvider provider = await CreateAsync(store);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var set = new DiagnosticOperationSet(scope.ServiceProvider.GetRequiredService<SettingsDiagnostics<SettingsDiagnosticModel>>());
        using var cancellation = new CancellationTokenSource();
        DiagnosticOperationResult receipt = Invoke(set, "save", Edit(), 0, cancellationToken: cancellation.Token);
        string id = receipt.Values!["job-id"].String!;
        Assert.Equal("Pending", Poll(set, id).Values!["write-status"].String);
        Assert.Equal("busy", Invoke(set, "save", Edit(), 0).Code);
        Assert.Equal(0.5, Invoke(set, "read").Values!["volume"].Double);
        Assert.Equal("not-found", Invoke(set, "save-result", new() { ["job-id"] = DiagnosticValue.From(id) }, session: Guid.NewGuid()).Code);
        cancellation.Cancel();
        Assert.Equal("Cancelled", (await WaitAsync(set, id)).Values!["write-status"].String);
        Assert.Null(store.Bytes);
        store.Gate.SetResult();
        receipt = Invoke(set, "save", Edit(), 0);
        Assert.Equal("Saved", (await WaitAsync(set, receipt.Values!["job-id"].String!)).Values!["write-status"].String);
        Assert.Equal("not-found", Poll(set, id).Code);
        DiagnosticOperationResult saved = Invoke(set, "read");
        Assert.Equal(0.8, saved.Values!["volume"].Double);
        Assert.Equal(1, saved.Revision);
        Assert.Contains("not-for-diagnostics", System.Text.Encoding.UTF8.GetString(store.Bytes!), StringComparison.Ordinal);
        Assert.Equal("default", saved.Values["device"].String);
        Assert.Equal(0, receipt.Revision);
    }

    /// <summary>Checks failed validation and storage never publish candidate values or error details.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task FailedWritesKeepCommittedSettingsAsync()
    {
        var store = new Store();
        await using ServiceProvider provider = await CreateAsync(store);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var set = new DiagnosticOperationSet(scope.ServiceProvider.GetRequiredService<SettingsDiagnostics<SettingsDiagnosticModel>>());
        Dictionary<string, DiagnosticValue> invalid = Edit();
        invalid["volume"] = DiagnosticValue.From(2.0);
        DiagnosticOperationResult receipt = Invoke(set, "save", invalid, 0);
        Assert.Equal("ValidationFailed", (await WaitAsync(set, receipt.Values!["job-id"].String!)).Values!["write-status"].String);
        store.Fail = true;
        receipt = Invoke(set, "save", Edit(), 0);
        DiagnosticOperationResult failure = await WaitAsync(set, receipt.Values!["job-id"].String!);
        Assert.Equal("StorageFailure", failure.Values!["write-status"].String);
        Assert.Null(failure.Message);
        Assert.Equal(0, failure.Revision);
        Assert.Equal(0.5, Invoke(set, "read").Values!["volume"].Double);
        Assert.Null(store.Bytes);
    }

    /// <summary>Checks a queued save still detects another writer's committed revision.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task QueuedSaveReportsConflictAsync()
    {
        var store = new Store { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using ServiceProvider provider = await CreateAsync(store);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IEditableOptions<SettingsDiagnosticModel> settings = provider.GetRequiredService<IEditableOptions<SettingsDiagnosticModel>>();
        SettingsEdit<SettingsDiagnosticModel> edit = settings.BeginEdit();
        edit.Value.Volume = 0.9;
        Task<SettingsSaveResult<SettingsDiagnosticModel>> external = settings.SaveAsync(edit);
        var set = new DiagnosticOperationSet(scope.ServiceProvider.GetRequiredService<SettingsDiagnostics<SettingsDiagnosticModel>>());
        DiagnosticOperationResult receipt = Invoke(set, "save", Edit(), 0);
        Assert.Equal("Pending", Poll(set, receipt.Values!["job-id"].String!).Values!["write-status"].String);
        store.Gate.SetResult();
        Assert.Equal(SettingsSaveStatus.Saved, (await external).Status);
        DiagnosticOperationResult result = await WaitAsync(set, receipt.Values["job-id"].String!);
        Assert.Equal("Conflict", result.Values!["write-status"].String);
        Assert.Equal(1, result.Revision);
        Assert.Equal(0.9, Invoke(set, "read").Values!["volume"].Double);
    }

    /// <summary>Checks game scope disposal cancels incomplete persistence.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task ScopeDisposalCancelsSaveAsync()
    {
        var store = new Store { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using ServiceProvider provider = await CreateAsync(store);
        AsyncServiceScope scope = provider.CreateAsyncScope();
        var set = new DiagnosticOperationSet(scope.ServiceProvider.GetRequiredService<SettingsDiagnostics<SettingsDiagnosticModel>>());
        DiagnosticOperationResult receipt = Invoke(set, "save", Edit(), 0);
        Assert.Equal("Pending", Poll(set, receipt.Values!["job-id"].String!).Values!["write-status"].String);
        await scope.DisposeAsync();
        Assert.Null(store.Bytes);
        Assert.Equal(0, provider.GetRequiredService<IEditableOptions<SettingsDiagnosticModel>>().Revision);
    }

    /// <summary>Checks protected documents require explicit recovery and are not overwritten.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task ProtectedDocumentRemainsUntouchedAsync()
    {
        byte[] original = System.Text.Encoding.UTF8.GetBytes("{\"documentVersion\":99,\"sections\":{}}");
        var store = new Store(original);
        await using ServiceProvider provider = await CreateAsync(store);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var set = new DiagnosticOperationSet(scope.ServiceProvider.GetRequiredService<SettingsDiagnostics<SettingsDiagnosticModel>>());
        Assert.Equal("UnsupportedVersion", Invoke(set, "read").Values!["load-status"].String);
        DiagnosticOperationResult receipt = Invoke(set, "save", Edit(), 0);
        Assert.Equal("RecoveryRequired", (await WaitAsync(set, receipt.Values!["job-id"].String!)).Values!["write-status"].String);
        Assert.Equal(original, store.Bytes);
    }

    /// <summary>Checks read-only registrations and rejected definitions.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task ReadOnlyAndInvalidDefinitionsAsync()
    {
        var store = new Store();
        PersistedSettingsSource source = await PersistedSettingsSource.LoadAsync(store);
        var services = new ServiceCollection();
        RegisterSettings(services, source);
        services.AddSettingsDiagnostics<SettingsDiagnosticModel, BeforeInputProcessing>("audio", fields => fields.Double("volume", model => model.Volume));
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var set = new DiagnosticOperationSet(scope.ServiceProvider.GetRequiredService<SettingsDiagnostics<SettingsDiagnosticModel>>());
        Assert.Equal("read", Assert.Single(set.Catalog).Id);
        Assert.Throws<ArgumentException>(() => services.AddSettingsDiagnostics<SettingsDiagnosticModel, BeforeInputProcessing>("other", fields => fields.Double("volume", model => model.Volume)));
        var fields = new SettingsDiagnosticFields<SettingsDiagnosticModel>();
        Assert.Throws<ArgumentException>(() => fields.String("load-status", model => model.Device));
        fields.Double("volume", model => model.Volume);
        Assert.Throws<ArgumentException>(() => fields.Boolean("volume", model => model.Enabled));
        fields.Seal();
        Assert.Throws<ArgumentException>(() => fields.Boolean("enabled", model => model.Enabled));
    }

    /// <summary>Checks both game transports use the same settings API against a running server.</summary>
    /// <param name="magicOnion">Whether to use StreamingHub.</param>
    /// <returns>The integration test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsRoundTripAsync(bool magicOnion)
    {
        string gameToken = Guid.NewGuid().ToString("N");
        string operatorToken = Guid.NewGuid().ToString("N");
        await using WebApplication server = DiagnosticServerApplication.Create([], options =>
        {
            options.HttpPort = 0;
            options.MagicOnionPort = 0;
            options.GameToken = gameToken;
            options.OperatorToken = operatorToken;
        });
        await server.StartAsync();
        Uri[] endpoints = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Select(address => new Uri(address)).ToArray();
        var store = new Store();
        PersistedSettingsSource source = await PersistedSettingsSource.LoadAsync(store);
        await using var game = new RemoteGame(endpoints[magicOnion ? 1 : 0], gameToken, magicOnion, services =>
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddPersistedSettings(source).Build());
            services.AddSettings(source);
            services.AddPersistedOptions<SettingsDiagnosticModel>("audio").UseJsonTypeInfo(SettingsDiagnosticJsonContext.Default.SettingsDiagnosticModel);
            Expose(services);
        });
        Guid session = await game.ReadyAsync();
        using var client = new HttpClient { BaseAddress = endpoints[0], Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
        DiagnosticOperationResult before = await RemoteInvokeAsync(client, session, "read", []);
        Assert.Equal(0.5, before.Values!["volume"].Double);
        Assert.False(before.Values.ContainsKey("secret"));
        DiagnosticOperationResult receipt = await RemoteInvokeAsync(client, session, "save", Edit(), before.Revision);
        string id = receipt.Values!["job-id"].String!;
        DiagnosticOperationResult result;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        do
        {
            result = await RemoteInvokeAsync(client, session, "save-result", new() { ["job-id"] = DiagnosticValue.From(id) });
            if (result.Values!["write-status"].String == "Pending")
            {
                await Task.Delay(10, timeout.Token);
            }
        }
        while (result.Values!["write-status"].String == "Pending");
        Assert.Equal("Saved", result.Values["write-status"].String);
        DiagnosticOperationResult after = await RemoteInvokeAsync(client, session, "read", []);
        Assert.Equal(0.8, after.Values!["volume"].Double);
        Assert.Equal(9007199254740993L, after.Values["count"].Int64);
        Assert.Equal(1, after.Revision);
        Assert.Equal("conflict", (await RemoteInvokeAsync(client, session, "save", Edit(), before.Revision)).Status);
        Assert.Contains("not-for-diagnostics", System.Text.Encoding.UTF8.GetString(store.Bytes!), StringComparison.Ordinal);
    }

    private static async Task<ServiceProvider> CreateAsync(Store store)
    {
        PersistedSettingsSource source = await PersistedSettingsSource.LoadAsync(store);
        var services = new ServiceCollection();
        RegisterSettings(services, source);
        Expose(services);
        return services.BuildServiceProvider();
    }

    private static void RegisterSettings(IServiceCollection services, PersistedSettingsSource source)
    {
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddPersistedSettings(source).Build());
        services.AddSettings(source);
        services.AddPersistedOptions<SettingsDiagnosticModel>("audio")
            .Validate(model => model.Volume is >= 0 and <= 1, "Secret validation details")
            .UseJsonTypeInfo(SettingsDiagnosticJsonContext.Default.SettingsDiagnosticModel);
        services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("input.before-processing");
    }

    private static void Expose(IServiceCollection services)
        => services.AddSettingsDiagnostics<SettingsDiagnosticModel, BeforeInputProcessing>("audio", fields => fields
            .Double("volume", model => model.Volume, (model, value) => model.Volume = value)
            .Boolean("enabled", model => model.Enabled, (model, value) => model.Enabled = value)
            .Int64("count", model => model.Count, (model, value) => model.Count = value)
            .String("device", model => model.Device));

    private static Dictionary<string, DiagnosticValue> Edit() => new()
    {
        ["volume"] = DiagnosticValue.From(0.8),
        ["enabled"] = DiagnosticValue.From(false),
        ["count"] = DiagnosticValue.From(9007199254740993L),
    };

    private static DiagnosticOperationResult Invoke(DiagnosticOperationSet set, string operation, Dictionary<string, DiagnosticValue>? arguments = null, long? revision = null, HashSet<DiagnosticPermission>? permissions = null, CancellationToken cancellationToken = default, Guid? session = null)
        => set.Invoke(operation, arguments ?? [], new(Guid.NewGuid(), session ?? _session, new(0, Stopwatch.GetTimestamp()), "test", revision, cancellationToken), permissions ?? _permissions);

    private static DiagnosticOperationResult Poll(DiagnosticOperationSet set, string id) => Invoke(set, "save-result", new() { ["job-id"] = DiagnosticValue.From(id) });

    private static async Task<DiagnosticOperationResult> WaitAsync(DiagnosticOperationSet set, string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        DiagnosticOperationResult result;
        do
        {
            result = Poll(set, id);
            if (result.Values!["write-status"].String == "Pending")
            {
                await Task.Delay(5, timeout.Token);
            }
        }
        while (result.Values!["write-status"].String == "Pending");
        return result;
    }

    private static async Task<DiagnosticOperationResult> RemoteInvokeAsync(HttpClient client, Guid session, string operation, Dictionary<string, DiagnosticValue> arguments, long? revision = null)
    {
        var invocation = new OperationInvocation(Guid.NewGuid(), "settings.audio", operation, arguments, revision);
        using var body = new StringContent(JsonSerializer.Serialize(invocation, DiagnosticJson.Context.OperationInvocation), System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync($"diagnostics/v1/sessions/{session}/operations", body);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), DiagnosticJson.Context.DiagnosticOperationResult)!;
    }

    private sealed class Store(byte[]? initial = null) : ISettingsStore
    {
        public byte[]? Bytes { get; private set; } = initial;

        public TaskCompletionSource? Gate { get; init; }

        public bool Fail { get; set; }

        public ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Bytes);

        public async ValueTask WriteAtomicallyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (Gate != null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Fail)
            {
                throw new IOException("Secret storage path");
            }

            Bytes = data.ToArray();
        }
    }
}
