using System.Text;

using Lumyte.Resources;
namespace Lumyte.DevTools.Host;

public sealed class DemoResourcesDomain : IAsyncDisposable
{
    public static readonly DevToolsDomain Domain = new("resources");
    public static readonly DevToolsQuery<ResourceSnapshotRequest, ResourceDomainSnapshot> GetState = new("getState");
    public static readonly DevToolsCommand<ResourceOperationRequest, ResourceCommandResult> Load = new("load");
    public static readonly DevToolsCommand<ResourceOperationRequest, ResourceCommandResult> Reload = new("reload");
    public static readonly DevToolsCommand<ResourceOperationRequest, ResourceCommandResult> Unload = new("unload");
    public static readonly DevToolsCommand<CollectResourcesRequest, ResourceCommandResult> Collect = new("collect");
    public static readonly DevToolsEvent<ResourceOperationEvent> Changed = new("operationChanged");
    private static readonly IReadOnlyDictionary<string, CatalogResource> Catalog = new Dictionary<string, CatalogResource>(StringComparer.Ordinal) { ["demo:palette"] = new("demo:palette", "DemoText", "violet,cyan,amber"), ["demo:scene"] = new("demo:scene", "DemoScene", "demo:palette|DevTools sample scene"), ["demo:overlay"] = new("demo:overlay", "DemoScene", "demo:palette|Shared palette overlay") };
    private readonly ResourceStore store = new([new DemoResolver(Catalog)], [new DemoTextLoader(), new DemoSceneLoader()]);
    private readonly Dictionary<string, IAsyncDisposable> rootPins = new(StringComparer.Ordinal);
    private readonly IDisposable[] registrations;
    private readonly DevToolsEventPublisher<ResourceOperationEvent> publisher;
    private readonly Lock sync = new();
    private ResourceOperationStatus? activeOperation;

    public DemoResourcesDomain(DevToolsHub hub)
    {
        publisher = hub.RegisterEvent(Domain, Changed);
        registrations = [hub.RegisterQuery(Domain, GetState, GetAsync), hub.RegisterCommand(Domain, Load, (request, token) => RunAsync("load", request.Id, token)), hub.RegisterCommand(Domain, Reload, (request, token) => RunAsync("reload", request.Id, token)), hub.RegisterCommand(Domain, Unload, (request, token) => RunAsync("unload", request.Id, token)), hub.RegisterCommand(Domain, Collect, (request, token) => RunAsync("collect", request.Id, token))];
    }
    public async ValueTask DisposeAsync() { foreach (IDisposable registration in registrations.Reverse()) { registration.Dispose(); } publisher.Dispose(); foreach (IAsyncDisposable pin in rootPins.Values) { await pin.DisposeAsync(); } rootPins.Clear(); await store.DisposeAsync(); }
    private ValueTask<ResourceDomainSnapshot> GetAsync(ResourceSnapshotRequest _, CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Snapshot()); }

    private async ValueTask<ResourceCommandResult> RunAsync(string operation, string? id, CancellationToken token)
    {
        if (operation != "collect" && (id is null || !Catalog.ContainsKey(id)))
        {
            return await FailAsync(operation, id, "not_in_catalog", $"Resource '{id}' is not in the typed demo catalog.", token);
        }

        SetOperation(new(operation, id, "running"));
        await publisher.PublishAsync(new(DateTimeOffset.UtcNow, operation, id, "running", null, null, Snapshot()), token);
        try
        {
            int unloaded = 0;
            if (operation == "load")
            { if (!rootPins.ContainsKey(id!)) { rootPins.Add(id!, await PinAsync(id!, token)); } }
            else if (operation == "reload")
            { if (!rootPins.ContainsKey(id!)) { return await FailAsync(operation, id, "not_loaded_root", "Reload requires a loaded root. Use Load first.", token); } await ReloadAsync(id!, token); }
            else if (operation == "unload")
            { if (!rootPins.Remove(id!, out IAsyncDisposable? pin)) { return await FailAsync(operation, id, "not_loaded_root", "Only an explicitly loaded root can be unloaded.", token); } await pin.DisposeAsync(); unloaded = (await store.CollectAsync(ResourceCollectionMode.AllUnused, token)).UnloadedResourceCount; }
            else
            { unloaded = (await store.CollectAsync(ResourceCollectionMode.AllUnused, token)).UnloadedResourceCount; }
            SetOperation(null);
            ResourceDomainSnapshot snapshot = Snapshot();
            ResourceCommandResult result = new(true, operation, id, null, $"{operation} completed.", unloaded, snapshot);
            await publisher.PublishAsync(new(DateTimeOffset.UtcNow, operation, id, "succeeded", null, result.Message, snapshot), token);
            return result;
        }
        catch (Exception exception) { return await FailAsync(operation, id, "operation_failed", exception.Message, CancellationToken.None); }
    }
    private async ValueTask<ResourceCommandResult> FailAsync(string operation, string? id, string code, string message, CancellationToken token) { SetOperation(null); ResourceDomainSnapshot snapshot = Snapshot(); ResourceCommandResult result = new(false, operation, id, code, message, 0, snapshot); await publisher.PublishAsync(new(DateTimeOffset.UtcNow, operation, id, "failed", code, message, snapshot), token); return result; }
    private void SetOperation(ResourceOperationStatus? operation) { lock (sync) { activeOperation = operation; } }
    private async ValueTask<IAsyncDisposable> PinAsync(string id, CancellationToken token) => Catalog[id].Type == "DemoScene" ? await store.PinAsync(Asset.From<DemoScene>(id), token) : await store.PinAsync(Asset.From<DemoText>(id), token);
    private async ValueTask ReloadAsync(string id, CancellationToken token) { if (Catalog[id].Type == "DemoScene") { await store.ReloadAsync(Asset.From<DemoScene>(id), token); } else { await store.ReloadAsync(Asset.From<DemoText>(id), token); } }

    private ResourceDomainSnapshot Snapshot()
    {
        ResourceStoreDiagnosticSnapshot diagnostic = store.GetDiagnosticSnapshot();
        var byId = diagnostic.Resources.ToDictionary(resource => resource.Id);
        var referenced = diagnostic.Resources.SelectMany(resource => resource.Dependencies).ToHashSet();
        HashSet<uint> rootEmitted = [];
        ResourceTreeNode[] roots = [.. rootPins.Keys.Order().Select(key => BuildNode(diagnostic.Resources.FirstOrDefault(resource => resource.Key == key), byId, [], rootEmitted))];
        HashSet<uint> allEmitted = [];
        ResourceTreeNode[] allLoaded = [.. diagnostic.Resources.Where(resource => !referenced.Contains(resource.Id)).OrderBy(resource => resource.Key).Select(resource => BuildNode(resource, byId, [], allEmitted))];
        ResourceOperationStatus? operation;
        lock (sync)
        { operation = activeOperation; }
        return new([.. Catalog.Values.Select(item => new CatalogResourceInfo(item.Key, item.Type))], roots, allLoaded, operation);
    }
    private static ResourceTreeNode BuildNode(ResourceDiagnosticEntry? resource, IReadOnlyDictionary<uint, ResourceDiagnosticEntry> byId, HashSet<uint> path, HashSet<uint> emitted)
    {
        if (resource is null)
        {
            return new(0, "missing", "Unknown", "missing", 0, 0, 0, false, null, [], "Resource metadata is missing.");
        }

        if (!path.Add(resource.Id))
        {
            return Node(resource, "cycle", true, resource.Id, [], "Dependency cycle detected.");
        }

        if (!emitted.Add(resource.Id))
        {
            return Node(resource, resource.State.ToString().ToLowerInvariant(), true, resource.Id, [], null);
        }

        ResourceTreeNode[] children = [.. resource.Dependencies.Select(id => byId.TryGetValue(id, out ResourceDiagnosticEntry? dependency) ? BuildNode(dependency, byId, [.. path], emitted) : new ResourceTreeNode(id, $"missing:{id}", "Unknown", "missing", 0, 0, 0, false, null, [], "Dependency is not loaded."))];
        return Node(resource, resource.State.ToString().ToLowerInvariant(), false, null, children, resource.Error);
    }
    private static ResourceTreeNode Node(ResourceDiagnosticEntry resource, string state, bool reference, uint? referenceTo, IReadOnlyList<ResourceTreeNode> children, string? error) => new(resource.Id, resource.Key, resource.ResourceType, state, resource.Generation, resource.MemoryCosts.Sum(cost => cost.Bytes), resource.ReferenceCount, reference, referenceTo, children, error);

    private sealed record CatalogResource(string Key, string Type, string Data);
    private sealed class DemoResolver(IReadOnlyDictionary<string, CatalogResource> catalog) : IAssetResolver { public string Scheme => "demo"; public ValueTask<AssetData> OpenAsync(AssetAddress address, CancellationToken token = default) { string key = $"demo:{address}"; if (!catalog.TryGetValue(key, out CatalogResource? item)) { throw new ResourceNotFoundException($"The demo catalog does not contain '{key}'."); } return ValueTask.FromResult(new AssetData(new MemoryStream(Encoding.UTF8.GetBytes(item.Data)), new("demo", address.ToString()))); } }
    private sealed record DemoText(string Value); private sealed record DemoScene(string Name, DemoText Palette);
    private sealed class DemoTextLoader : IResourceLoader<DemoText> { public async ValueTask<DemoText> LoadAsync(ResourceLoadContext context, CancellationToken token = default) { using var reader = new StreamReader(context.Content, Encoding.UTF8, leaveOpen: true); return new(await reader.ReadToEndAsync(token)); } }
    private sealed class DemoSceneLoader : IResourceLoader<DemoScene> { public async ValueTask<DemoScene> LoadAsync(ResourceLoadContext context, CancellationToken token = default) { using var reader = new StreamReader(context.Content, Encoding.UTF8, leaveOpen: true); string[] parts = (await reader.ReadToEndAsync(token)).Split('|', 2); ResourceDependency<DemoText> dependency = await context.LoadAsync(Asset.From<DemoText>(parts[0]), token); return new(parts[1], dependency.Value); } }
}
public sealed record ResourceSnapshotRequest;
public sealed record ResourceOperationRequest(string Id);
public sealed record CollectResourcesRequest(string? Id = null);
public sealed record CatalogResourceInfo(string Key, string Type);
public sealed record ResourceOperationStatus(string Operation, string? Key, string State);
public sealed record ResourceTreeNode(uint Id, string Key, string Type, string State, uint Generation, long MemoryBytes, int ReferenceCount, bool IsReference, uint? ReferenceTo, IReadOnlyList<ResourceTreeNode> Dependencies, string? Error);
public sealed record ResourceDomainSnapshot(IReadOnlyList<CatalogResourceInfo> Catalog, IReadOnlyList<ResourceTreeNode> Roots, IReadOnlyList<ResourceTreeNode> AllLoaded, ResourceOperationStatus? ActiveOperation);
public sealed record ResourceCommandResult(bool Success, string Operation, string? Key, string? ErrorCode, string Message, int UnloadedCount, ResourceDomainSnapshot Snapshot);
public sealed record ResourceOperationEvent(DateTimeOffset Timestamp, string Operation, string? Key, string State, string? ErrorCode, string? Message, ResourceDomainSnapshot Snapshot);
