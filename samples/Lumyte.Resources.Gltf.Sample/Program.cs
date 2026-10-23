using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Embedded;
using Lumyte.Resources.FileSystem;
using Lumyte.Resources.Graphics;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Gltf.Sample;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lumyte-gltf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string assets = Path.Combine(AppContext.BaseDirectory, "Assets");
            foreach (string source in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(directory, Path.GetRelativePath(assets, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }

            await RunAsync(directory, args);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task RunAsync(string directory, string[] args)
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddSingleton(new FileResourceSourceOptions { RootPath = directory });
        services.AddSingleton(new EmbeddedResourceSourceOptions
        {
            Assembly = typeof(Program).Assembly,
            ResourceNames = new Dictionary<ResourceId, string>
            {
                [new("shaders/vertex")] = "Lumyte.Gltf.vertex.lshader",
                [new("shaders/fragment")] = "Lumyte.Gltf.fragment.lshader",
            },
        });
        services.AddScoped<FileResourceSource>();
        services.AddScoped<EmbeddedResourceSource>();
        services.AddGraphicsDevice<DeviceFactory>("main");
        services.AddResources("gltf.validation", pipelines => ConfigurePipelines(pipelines.ConfigureHotReload(new() { Enabled = false })));
        services.AddResources("gltf", ConfigurePipelines);
        services.AddResourceGraphics("gltf", "main", "graphics.main");
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, Environment.ProcessorCount, new ExecutionOptions());
        await execution.StartAsync();
        try
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            ResourceManager validation = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("gltf.validation");

            await ValidateCpuAsync(validation, directory);
            await validation.TrimAsync();
            await validation.DisposeAsync();
            IGraphicsDeviceService devices = scope.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
            Require(devices.State.Status == GraphicsDeviceServiceStatus.Uninitialized, "CPU scene loading initialized a graphics device.");
            if (!args.Contains("--validate", StringComparer.Ordinal))
            {
                await devices.InitializeAsync();
                ResourceManager resources = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("gltf");
                ResourceReloadService changes = resources.HotReload!;
                string output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "gltf-render.png");
                await RenderAndReloadAsync(resources, changes, devices, directory, output);
                await resources.TrimAsync();
                await resources.DisposeAsync();
            }

            IMemoryManager memory = provider.GetRequiredService<IMemoryManager>();
            foreach (string pool in new[] { "gltf.cpu", "gltf.gpu", "gltf.upload", "resources.source" })
            {
                ulong committed = memory.GetSnapshot(new(pool)).CommittedBytes;
                Require(committed == 0, $"Pool {pool} retained {committed} bytes after release.");
            }

            Console.WriteLine("glTF checks passed; all resource memory charges returned.");
        }
        finally
        {
            await execution.StopAsync();
        }
    }

    private static void ConfigurePipelines(ResourceRegistrationBuilder pipelines)
    {
        pipelines
            .UseSource<SampleSource>()
            .AddSourcePipeline<GltfDocument, DocumentStage>()
            .AddSourcePipeline<SourceBytes, BinaryStage>()
            .AddSourcePipeline<CpuImage, ImageStage>()
            .AddSourcePipeline<ShaderArtifact, ShaderStage>()
            .AddTransformPipeline<GltfDocument, CpuMaterial, MaterialStage>()
            .AddTransformPipeline<GltfDocument, CpuMesh, MeshStage>()
            .AddTransformPipeline<CpuImage, GpuTexture, TextureUploadStage>()
            .AddTransformPipeline<CpuMesh, GpuMesh, MeshUploadStage>()
            .AddTransformPipeline<GltfDocument, CpuScene, SceneStage>()
            .AddTransformPipeline<CpuScene, GpuScene, SceneUploadStage>();
    }

    private static async Task ValidateCpuAsync(ResourceManager resources, string directory)
    {
        var sceneKey = new ResourceKey<CpuScene>("models/cubes.gltf", "scene/0");
        using ResourceReference<CpuScene> sceneReference = await resources.AcquireAsync(sceneKey);
        using ResourceLease<CpuScene> scene = sceneReference.Acquire();
        Require(scene.Value.Roots.Count == 2 && ReferenceEquals(scene.Value.Roots[0].Mesh, scene.Value.Roots[1].Mesh), "CPU scene instances did not share the decoded mesh.");
        Require(scene.Value.Roots[0].World.Translation.X == -0.8f && scene.Value.Roots[1].World.Translation.X == 0.85f, "CPU scene transforms were not assembled.");
        await resources.TrimAsync();
        Require(scene.Value.Roots[0].Mesh!.Material.Texture.Pixels.Memory.Length == 64 * 64 * 4, "CPU scene did not retain its decoded texture after trimming.");
        var key = new ResourceKey<CpuMesh>("models/cubes.gltf", "mesh/0");
        using ResourceReference<CpuMesh> meshReference = await resources.AcquireAsync(key);
        using ResourceLease<CpuMesh> mesh = meshReference.Acquire();
        Require(mesh.Value.Vertices.Memory.Length == 36, "Expected the cube's twelve triangles.");
        Require(mesh.Value.Material.Texture.Id.Value == "textures/checker.png", "External texture URI did not resolve relative to the document.");
        using ResourceReference<CpuImage> imageReference = await resources.AcquireAsync(new ResourceKey<CpuImage>("textures/checker.png"));
        using ResourceLease<CpuImage> image = imageReference.Acquire();
        Require(ReferenceEquals(mesh.Value.Material.Texture, image.Value), "The material did not retain the shared CPU image.");
        Require(image.Value.Width == 64 && image.Value.Height == 64, "External PNG did not decode.");

        // Invalid external data must fail a transaction without replacing the existing generation.
        string path = Path.Combine(directory, "models", "cubes.bin");
        byte[] original = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path, original[..8]);
        try
        {
            await ReloadForValidationAsync(resources, [new("models/cubes.bin")]);
            throw new InvalidOperationException("A truncated external buffer was published.");
        }
        catch (InvalidDataException)
        {
            // The failed candidate is discarded; the published generation remains usable.
        }

        using ResourceReference<CpuMesh> retainedReference = await resources.AcquireAsync(key);
        using ResourceLease<CpuMesh> retained = retainedReference.Acquire();
        Require(ReferenceEquals(mesh.Value, retained.Value), "Failed reload replaced the valid mesh.");
        await File.WriteAllBytesAsync(path, original);
        await ValidateDocumentFailuresAsync(resources, directory, key, mesh.Value);
        byte[] changed = (byte[])original.Clone();
        BinaryPrimitives.WriteSingleLittleEndian(changed, -0.5f);
        await File.WriteAllBytesAsync(path, changed);
        ResourceReloadResult updated = await ReloadForValidationAsync(resources, [new("models/cubes.bin")]);
        Require(updated.Status == ResourceReloadStatus.Published, "External BIN changes did not rebuild the dependent mesh.");
        using (ResourceLease<CpuMesh> current = meshReference.Acquire())
        {
            Require(current.Value.Vertices.Memory.Span[0].Position.X == -0.5f && mesh.Value.Vertices.Memory.Span[0].Position.X == -1, "BIN reload did not isolate mesh generations.");
        }

        await File.WriteAllBytesAsync(path, original);
        await ReloadForValidationAsync(resources, [new("models/cubes.bin")]);
        await ValidateSceneHierarchyAsync(resources, directory, sceneKey);
        Console.WriteLine("CPU scene: hierarchy, transforms, shared meshes/materials and decoded PNGs assembled without a GPU; reload preserved old generations.");
    }

    private static async Task ValidateSceneHierarchyAsync(ResourceManager resources, string directory, ResourceKey<CpuScene> key)
    {
        string path = Path.Combine(directory, "models", "cubes.gltf");
        string original = await File.ReadAllTextAsync(path);
        JsonNode document = JsonNode.Parse(original)!;
        document["scenes"]![0]!["nodes"] = new JsonArray(2);
        document["nodes"]!.AsArray().Add(new JsonObject
        {
            ["translation"] = new JsonArray(1, 2, 3),
            ["children"] = new JsonArray(0, 1),
        });
        await File.WriteAllTextAsync(path, document.ToJsonString());
        await ReloadForValidationAsync(resources, [new("models/cubes.gltf")]);
        using ResourceReference<CpuScene> hierarchyReference = await resources.AcquireAsync(key);
        using ResourceLease<CpuScene> hierarchy = hierarchyReference.Acquire();
        CpuSceneNode root = hierarchy.Value.Roots.Single();
        Require(root.Mesh is null && root.Children.Count == 2, "CPU scene did not preserve the non-mesh parent and child hierarchy.");
        Require(root.Children[0].Local.Translation.X == -0.8f && Vector3.Distance(root.Children[0].World.Translation, new(0.2f, 2, 3)) < 0.00001f, "CPU scene did not compose local and world transforms.");
        Require(ReferenceEquals(root.Children[0].Mesh, root.Children[1].Mesh), "Nested CPU scene instances duplicated the mesh.");
        document["nodes"]![0]!["children"] = new JsonArray(2);
        await File.WriteAllTextAsync(path, document.ToJsonString());
        try
        {
            await ReloadForValidationAsync(resources, [new("models/cubes.gltf")]);
            throw new InvalidOperationException("A cyclic CPU scene was published.");
        }
        catch (InvalidDataException)
        {
            using ResourceReference<CpuScene> retainedReference = await resources.AcquireAsync(key);
            using ResourceLease<CpuScene> retained = retainedReference.Acquire();
            Require(ReferenceEquals(hierarchy.Value, retained.Value), "Failed hierarchy reload replaced the CPU scene.");
        }

        document["nodes"]![0]!.AsObject().Remove("children");
        await File.WriteAllTextAsync(path, document.ToJsonString());
        string imagePath = Path.Combine(directory, "textures", "checker.png");
        byte[] image = await File.ReadAllBytesAsync(imagePath);
        File.Delete(imagePath);
        try
        {
            await ReloadForValidationAsync(resources, [new("textures/checker.png")]);
            throw new InvalidOperationException("A CPU scene with a missing image was published.");
        }
        catch (FileNotFoundException)
        {
            Require(root.Children[0].Mesh!.Material.Texture.Pixels.Memory.Span[0] == 240, "Failed image reload invalidated the retained CPU image.");
        }

        await File.WriteAllBytesAsync(imagePath, image);
        await File.WriteAllTextAsync(path, original);
        await ReloadForValidationAsync(resources, [new("models/cubes.gltf"), new("textures/checker.png")]);
    }

    private static async Task ValidateDocumentFailuresAsync(ResourceManager resources, string directory, ResourceKey<CpuMesh> key, CpuMesh expected)
    {
        string path = Path.Combine(directory, "models", "cubes.gltf");
        string original = await File.ReadAllTextAsync(path);
        Action<JsonNode>[] invalidDocuments =
        [
            root => root["buffers"]![0]!["uri"] = "../../outside.bin",
            root => root["bufferViews"]![0]!["byteLength"] = 4,
            root => root["accessors"]![0]!["normalized"] = true,
            root => root["meshes"]![0]!["primitives"]![0]!["mode"] = 1,
        ];
        foreach (Action<JsonNode> mutate in invalidDocuments)
        {
            JsonNode root = JsonNode.Parse(original)!;
            mutate(root);
            await File.WriteAllTextAsync(path, root.ToJsonString());
            try
            {
                await ReloadForValidationAsync(resources, [new("models/cubes.gltf")]);
                throw new InvalidOperationException("An unsupported or out-of-range glTF document was published.");
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            {
                using ResourceReference<CpuMesh> retainedReference = await resources.AcquireAsync(key);
                using ResourceLease<CpuMesh> retained = retainedReference.Acquire();
                Require(ReferenceEquals(expected, retained.Value), "A rejected document replaced the published mesh.");
            }
        }

        await File.WriteAllTextAsync(path, original);
    }

    private static async Task RenderAndReloadAsync(ResourceManager resources, ResourceReloadService changes, IGraphicsDeviceService devices, string directory, string output)
    {
        var key = new ResourceKey<GpuScene>("models/cubes.gltf", "scene/0");
        using ResourceReference<GpuScene> owner = await resources.AcquireAsync(key);
        using ResourceLease<GpuScene> original = owner.Acquire();
        Require(original.Value.Draws.Count == 2 && ReferenceEquals(original.Value.Draws[0].Mesh, original.Value.Draws[1].Mesh), "Scene instances did not share the uploaded mesh.");
        using GraphicsDeviceLease device = await devices.AcquireAsync();
        byte[] before = await SceneRenderer.RenderAsync(device.Device, original.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        PngFile.Write(output, 512, 512, before);
        Console.WriteLine($"Rendered external-texture glTF: {output}");

        TaskCompletionSource published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReloaded(object? sender, ResourceReloadEventArgs args)
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        }

        void OnReloadFailed(object? sender, ResourceReloadFailureEventArgs args)
        {
            if (!args.WillRetry)
            {
                published.TrySetException(args.Error);
            }
        }

        changes.Reloaded += OnReloaded;
        changes.ReloadFailed += OnReloadFailed;
        try
        {
            byte[] pixels = new byte[64 * 64 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = (byte)(255 - (((i / 4 / 8) % 2) * 100));
                pixels[i + 1] = 40;
                pixels[i + 2] = 100;
                pixels[i + 3] = 255;
            }

            PngFile.Write(Path.Combine(directory, "textures", "checker.png"), 64, 64, pixels);
            await published.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Require(owner.TryAcquire(out ResourceLease<GpuScene>? current), "The logical scene handle was lost during texture reload.");
            using (current)
            {
                byte[] after = await SceneRenderer.RenderAsync(device.Device, current!.Value);
                using ResourceReference<CpuScene> cpuReference = await resources.AcquireAsync(new ResourceKey<CpuScene>(key.Address));
                using ResourceLease<CpuScene> cpu = cpuReference.Acquire();
                Require(cpu.Value.Roots[0].Mesh!.Material.Texture.Pixels.Memory.Span[0] == 255, "Texture reload did not update the CPU scene.");
                Require(!after.AsSpan().SequenceEqual(before), "Changing only the external PNG did not alter the GPU scene.");
                byte[] old = await SceneRenderer.RenderAsync(device.Device, original.Value);
                Require(old.AsSpan().SequenceEqual(before), "Texture reload mutated the old leased GPU generation.");
                PngFile.Write(Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-reloaded.png"), 512, 512, after);
            }

            Console.WriteLine("Automatic PNG reload propagated image → CPU material/mesh/scene → GPU scene; the same handle resolved the new image and the old lease rendered unchanged.");
        }
        finally
        {
            changes.Reloaded -= OnReloaded;
            changes.ReloadFailed -= OnReloadFailed;
        }
    }

    private static ValueTask<ResourceReloadResult> ReloadForValidationAsync(ResourceManager manager, IReadOnlyCollection<ResourceId> sources)
    {
        MethodInfo command = typeof(ResourceManager).GetMethod("ReloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            return (ValueTask<ResourceReloadResult>)command.Invoke(manager, [sources, CancellationToken.None])!;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
