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
            .AddSourcePipeline<SceneDocument, SceneDocumentStage>("scene.document")
            .AddTransformPipeline<SceneDocument, MeshInfo, MeshStage>("mesh.cpu", "scene.document")
            .AddTransformPipeline<SceneDocument, MaterialInfo, MaterialStage>("material.info", "scene.document"));

        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope world = provider.CreateAsyncScope();
        ResourceManager resources = world.ServiceProvider.GetRequiredKeyedService<ResourceManager>("scene");
        SampleCounters counters = provider.GetRequiredService<SampleCounters>();
        var sourceId = new ResourceId("scene.json");
        var documentKey = new ResourceKey<SceneDocument>(new(sourceId), "scene.document");
        using (ResourceLease<SceneDocument> document = await resources.AcquireAsync(documentKey).ConfigureAwait(false))
        {
            Console.WriteLine($"Document elements: {string.Join(", ", document.Value.Subresources.Select(element => element.Selector))}");
            Console.WriteLine($"Projection builds during discovery: {counters.ProjectionBuilds}");
        }

        await DemonstrateReloadAsync(resources, sourceId, path, counters).ConfigureAwait(false);
        await resources.TrimAsync().ConfigureAwait(false);
        await resources.DisposeAsync().ConfigureAwait(false);
        IMemoryManager memory = provider.GetRequiredService<IMemoryManager>();
        Console.WriteLine($"Document buffer after release: {memory.GetSnapshot(new("sample.document")).CommittedBytes} bytes");
    }

    private static async Task DemonstrateReloadAsync(ResourceManager resources, ResourceId sourceId, string path, SampleCounters counters)
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
        await File.WriteAllTextAsync(path, UpdatedScene).ConfigureAwait(false);
        ResourceReloadResult reload = await resources.ReloadAsync([sourceId]).ConfigureAwait(false);
        if (reload.Status != ResourceReloadStatus.Published)
        {
            throw new InvalidOperationException($"Expected publication, got {reload.Status}.");
        }

        using ResourceReadScope currentView = resources.BeginRead();
        using ResourceLease<MeshInfo> newMesh = AcquireFrom(currentView, meshHandle);
        using ResourceLease<MaterialInfo> oldMaterial = AcquireFrom(originalView, materialHandle);
        using ResourceLease<MaterialInfo> newMaterial = AcquireFrom(currentView, materialHandle);
        Console.WriteLine($"Reload: {reload.Status}; publication {reload.PublicationVersion}; document builds: {counters.DocumentBuilds}");
        Console.WriteLine($"Existing lease: {oldMesh.Value.VertexCount} vertices; same handle after reload: {newMesh.Value.VertexCount} vertices");
        Console.WriteLine($"Original view: {oldMaterial.Value.Name}; current view: {newMaterial.Value.Name}");
    }

    private static ResourceLease<T> AcquireFrom<T>(ResourceReadScope view, ResourceHandle<T> handle)
        where T : class
        => view.TryAcquire(handle, out ResourceLease<T>? lease)
            ? lease!
            : throw new InvalidOperationException("The sample's owned handle has no published generation.");
}
