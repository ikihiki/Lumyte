using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Sample;

internal static class Program
{
    private static async Task Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lumyte-resources-sample-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await RunAsync(directory).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task RunAsync(string directory)
    {
        string path = Path.Combine(directory, "scene.json");
        const string InitialScene = """
            { "meshes": [ { "vertexCount": 3 } ], "materials": [ { "name": "copper" } ] }
            """;
        await File.WriteAllTextAsync(path, InitialScene).ConfigureAwait(false);

        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddSingleton(new FileResourceSourceOptions
        {
            RootPath = directory,
            SnapshotDirectory = Path.Combine(directory, "snapshots"),
        });
        services.AddSingleton<SampleCounters>();
        services.AddSingleton(new SampleSettings(new("sample.document")));
        services.AddResources("scene", resources => resources
            .UseSource<FileResourceSource>()
            .WatchChanges<FileResourceChangeSource>()
            .AddSourcePipeline<SceneDocument, SceneDocumentStage>("scene.document")
            .AddTransformPipeline<SceneDocument, MeshInfo, MeshStage>("mesh.cpu", "scene.document")
            .AddTransformPipeline<SceneDocument, MaterialInfo, MaterialStage>("material.info", "scene.document"));

        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope world = provider.CreateAsyncScope();
        ResourceManager resources = world.ServiceProvider.GetRequiredKeyedService<ResourceManager>("scene");
        ResourceReloadService changes = world.ServiceProvider.GetRequiredKeyedService<ResourceReloadService>("scene");
        SampleCounters counters = provider.GetRequiredService<SampleCounters>();
        var sourceId = new ResourceId("scene.json");
        var documentKey = new ResourceKey<SceneDocument>(new(sourceId), "scene.document");
        using (ResourceLease<SceneDocument> document = await resources.AcquireAsync(documentKey).ConfigureAwait(false))
        {
            Console.WriteLine($"Document elements: {string.Join(", ", document.Value.Subresources.Select(element => element.Selector))}");
            Console.WriteLine($"Projection builds during discovery: {counters.ProjectionBuilds}");
        }

        await DemonstrateReloadAsync(resources, changes, sourceId, path, counters).ConfigureAwait(false);
        await resources.TrimAsync().ConfigureAwait(false);
        await resources.DisposeAsync().ConfigureAwait(false);
        IMemoryManager memory = provider.GetRequiredService<IMemoryManager>();
        Console.WriteLine($"Document buffer after release: {memory.GetSnapshot(new("sample.document")).CommittedBytes} bytes");
    }

    private static async Task DemonstrateReloadAsync(ResourceManager resources, ResourceReloadService changes, ResourceId sourceId, string path, SampleCounters counters)
    {
        var meshKey = new ResourceKey<MeshInfo>(new(sourceId, "mesh/0", "geometry:v1"), "mesh.cpu");
        var materialKey = new ResourceKey<MaterialInfo>(new(sourceId, "material/0", "preview:v1"), "material.info");
        using ResourceReference<MeshInfo> meshOwner = await resources.CreateReferenceAsync(meshKey).ConfigureAwait(false);
        using ResourceReference<MaterialInfo> materialOwner = await resources.CreateReferenceAsync(materialKey).ConfigureAwait(false);
        ResourceHandle<MeshInfo> meshHandle = meshOwner.Handle;
        ResourceHandle<MaterialInfo> materialHandle = materialOwner.Handle;

        using ResourceLease<MeshInfo> oldMesh = await resources.AcquireAsync(meshKey).ConfigureAwait(false);
        using ResourceReadScope originalView = resources.BeginRead();
        Console.WriteLine($"Initial mesh: {oldMesh.Value.VertexCount} vertices; shared document builds: {counters.DocumentBuilds}");
        const string UpdatedScene = """
            { "meshes": [ { "vertexCount": 6 } ], "materials": [ { "name": "silver" } ] }
            """;
        ResourceReloadResult reload = await RewriteAndWaitAsync(changes, path, UpdatedScene).ConfigureAwait(false);

        using ResourceReadScope currentView = resources.BeginRead();
        using ResourceLease<MeshInfo> newMesh = AcquireFrom(currentView, meshHandle);
        using ResourceLease<MaterialInfo> oldMaterial = AcquireFrom(originalView, materialHandle);
        using ResourceLease<MaterialInfo> newMaterial = AcquireFrom(currentView, materialHandle);
        Console.WriteLine($"Automatic reload: {reload.Status}; publication {reload.PublicationVersion}; document builds: {counters.DocumentBuilds}");
        Console.WriteLine($"Existing lease: {oldMesh.Value.VertexCount} vertices; same handle after reload: {newMesh.Value.VertexCount} vertices");
        Console.WriteLine($"Original view: {oldMaterial.Value.Name}; current view: {newMaterial.Value.Name}");
    }

    private static async Task<ResourceReloadResult> RewriteAndWaitAsync(ResourceReloadService changes, string path, string contents)
    {
        TaskCompletionSource<ResourceReloadResult> initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<ResourceReloadResult> published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        changes.Reloaded += (_, args) =>
        {
            initialized.TrySetResult(args.Result);
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult(args.Result);
            }
        };
        changes.Start();
        try
        {
            await WaitForReloadAsync(changes, initialized.Task).ConfigureAwait(false);
            await File.WriteAllTextAsync(path, contents).ConfigureAwait(false);
            return await WaitForReloadAsync(changes, published.Task).ConfigureAwait(false);
        }
        finally
        {
            await changes.StopAsync().ConfigureAwait(false);
        }
    }

    private static async Task<ResourceReloadResult> WaitForReloadAsync(ResourceReloadService changes, Task<ResourceReloadResult> expected)
    {
        Task completed = await Task.WhenAny(expected, changes.Completion).WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        if (completed == changes.Completion)
        {
            await changes.Completion.ConfigureAwait(false);
            throw new InvalidOperationException("File monitoring ended before completing the expected reload.");
        }

        return await expected.ConfigureAwait(false);
    }

    private static ResourceLease<T> AcquireFrom<T>(ResourceReadScope view, ResourceHandle<T> handle)
        where T : class
        => view.TryAcquire(handle, out ResourceLease<T>? lease)
            ? lease!
            : throw new InvalidOperationException("The sample's owned handle has no published generation.");
}
