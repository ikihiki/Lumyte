using Lumyte.Execution;
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
        });
        services.AddSingleton<SampleCounters>();
        services.AddSingleton(new SampleSettings(new("sample.document")));
        services.AddResources(resources => resources
            .UseSource<FileResourceSource>()
            .AddSourcePipeline<SceneDocument, SceneDocumentStage>()
            .AddTransformPipeline<SceneDocument, MeshInfo, MeshStage>()
            .AddTransformPipeline<SceneDocument, MaterialInfo, MaterialStage>()
            .AddTransformPipeline<SceneDocument, SceneResource, SceneStage>());

        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, Environment.ProcessorCount, new ExecutionOptions());
        await execution.StartAsync().ConfigureAwait(false);
        try
        {
            await using ResourceManager resources = provider.GetRequiredService<ResourceManager>();
            SampleCounters counters = provider.GetRequiredService<SampleCounters>();
            using (ResourceReference<SceneResource> sceneReference = await resources.RetainAsync(new ResourceKey<SceneResource>("scene.json")).ConfigureAwait(false))
            using (ResourceLease<SceneResource> scene = sceneReference.Pin())
            {
                Console.WriteLine($"Scene: {scene.Value.Mesh.VertexCount} vertices; material: {scene.Value.Material.Name}");
                Console.WriteLine($"Projection builds: {counters.ProjectionBuilds}");
            }

            await DemonstrateReloadAsync(resources, "scene.json", path, counters).ConfigureAwait(false);
            await resources.TrimAsync().ConfigureAwait(false);
            await resources.DisposeAsync().ConfigureAwait(false);
            IMemoryManager memory = provider.GetRequiredService<IMemoryManager>();
            Console.WriteLine($"Document buffer after release: {memory.GetSnapshot(new("sample.document")).CommittedBytes} bytes");
        }
        finally
        {
            await execution.StopAsync().ConfigureAwait(false);
        }
    }

    private static async Task DemonstrateReloadAsync(ResourceManager resources, string sourceId, string path, SampleCounters counters)
    {
        var meshKey = new ResourceKey<MeshInfo>(sourceId, "mesh/0", "geometry:v1");
        var materialKey = new ResourceKey<MaterialInfo>(sourceId, "material/0", "preview:v1");
        using ResourceReference<MeshInfo> meshOwner = await resources.RetainAsync(meshKey).ConfigureAwait(false);
        using ResourceReference<MaterialInfo> materialOwner = await resources.RetainAsync(materialKey).ConfigureAwait(false);
        ResourceHandle<MeshInfo> meshHandle = meshOwner.Handle;
        ResourceHandle<MaterialInfo> materialHandle = materialOwner.Handle;

        using ResourceReference<SceneResource> sceneOwner = await resources.RetainAsync(new ResourceKey<SceneResource>(sourceId)).ConfigureAwait(false);
        using ResourceLease<SceneResource> oldScene = sceneOwner.Pin();
        using ResourceLease<MeshInfo> oldMesh = meshOwner.Pin();
        using ResourceSnapshot originalSnapshot = resources.CaptureSnapshot();
        Console.WriteLine($"Initial mesh: {oldMesh.Value.VertexCount} vertices; shared document builds: {counters.DocumentBuilds}");
        const string UpdatedScene = """
            { "meshes": [ { "vertexCount": 6 } ], "materials": [ { "name": "silver" } ] }
            """;
        ResourceReloadResult reload = await RewriteAndWaitAsync(resources, path, UpdatedScene).ConfigureAwait(false);

        using ResourceSnapshot currentSnapshot = resources.CaptureSnapshot();
        using ResourceLease<MeshInfo> newMesh = AcquireFrom(currentSnapshot, meshHandle);
        using ResourceLease<MaterialInfo> oldMaterial = AcquireFrom(originalSnapshot, materialHandle);
        using ResourceLease<MaterialInfo> newMaterial = AcquireFrom(currentSnapshot, materialHandle);
        Console.WriteLine($"Automatic reload: {reload.Status}; publication {reload.PublicationVersion}; document builds: {counters.DocumentBuilds}");
        Console.WriteLine($"Existing lease: {oldMesh.Value.VertexCount} vertices; same handle after reload: {newMesh.Value.VertexCount} vertices");
        using ResourceLease<SceneResource> newScene = AcquireFrom(currentSnapshot, sceneOwner.Handle);
        if (!ReferenceEquals(oldScene.Value.Mesh, oldMesh.Value) || !ReferenceEquals(newScene.Value.Mesh, newMesh.Value) ||
            !ReferenceEquals(oldScene.Value.Material, oldMaterial.Value) || !ReferenceEquals(newScene.Value.Material, newMaterial.Value))
        {
            throw new InvalidOperationException("Composite resource references did not retain their matching generations.");
        }

        Console.WriteLine($"Original view: {oldMaterial.Value.Name}; current view: {newMaterial.Value.Name}");
    }

    private static async Task<ResourceReloadResult> RewriteAndWaitAsync(ResourceManager resources, string path, string contents)
    {
        TaskCompletionSource<ResourceReloadResult> published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReloaded(object? sender, ResourceReloadEventArgs args)
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult(args.Result);
            }
        }

        resources.Reloaded += OnReloaded;
        try
        {
            await File.WriteAllTextAsync(path, contents).ConfigureAwait(false);
            return await WaitForReloadAsync(resources, published.Task).ConfigureAwait(false);
        }
        finally
        {
            resources.Reloaded -= OnReloaded;
        }
    }

    private static async Task<ResourceReloadResult> WaitForReloadAsync(ResourceManager resources, Task<ResourceReloadResult> expected)
    {
        Task completed = await Task.WhenAny(expected, resources.HotReloadCompletion).WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        if (completed == resources.HotReloadCompletion)
        {
            await resources.HotReloadCompletion.ConfigureAwait(false);
            throw new InvalidOperationException("File monitoring ended before completing the expected reload.");
        }

        return await expected.ConfigureAwait(false);
    }

    private static ResourceLease<T> AcquireFrom<T>(ResourceSnapshot snapshot, ResourceHandle<T> handle)
        where T : class
        => snapshot.TryPin(handle, out ResourceLease<T>? lease)
            ? lease!
            : throw new InvalidOperationException("The sample's owned handle has no published generation.");
}
