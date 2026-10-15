using System.Buffers.Binary;
using System.Text.Json.Nodes;
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
        services.AddResources("gltf", pipelines => pipelines
            .UseSource<SampleSource>()
            .WatchChanges<FileResourceChangeSource>()
            .AddSourcePipeline<GltfDocument, DocumentStage>()
            .AddSourcePipeline<SourceBytes, BinaryStage>()
            .AddSourcePipeline<CpuImage, ImageStage>()
            .AddSourcePipeline<ShaderArtifact, ShaderStage>()
            .AddTransformPipeline<GltfDocument, CpuMaterial, MaterialStage>()
            .AddTransformPipeline<GltfDocument, CpuMesh, MeshStage>()
            .AddTransformPipeline<CpuImage, GpuTexture, TextureUploadStage>()
            .AddTransformPipeline<CpuMesh, GpuMesh, MeshUploadStage>()
            .AddTransformPipeline<GltfDocument, GpuScene, SceneStage>());
        services.AddResourceGraphics("gltf", "main", "graphics.main");
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceManager resources = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("gltf");
        using (ResourceLease<GltfDocument> document = await resources.AcquireAsync(new ResourceKey<GltfDocument>("models/cubes.gltf")))
        {
            Console.WriteLine($"glTF subresources: {string.Join(", ", document.Value.Subresources.Select(item => item.Selector))}");
        }

        await ValidateCpuAsync(resources, directory);
        if (!args.Contains("--validate", StringComparer.Ordinal))
        {
            IGraphicsDeviceService devices = scope.ServiceProvider.GetRequiredKeyedService<IGraphicsDeviceService>("main");
            await devices.InitializeAsync();
            ResourceReloadService changes = scope.ServiceProvider.GetRequiredKeyedService<ResourceReloadService>("gltf");
            string output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "gltf-render.png");
            await RenderAndReloadAsync(resources, changes, devices, directory, output);
        }

        await resources.TrimAsync();
        await resources.DisposeAsync();
        IMemoryManager memory = provider.GetRequiredService<IMemoryManager>();
        foreach (string pool in new[] { "gltf.cpu", "gltf.gpu", "gltf.upload", "resources.source" })
        {
            ulong committed = memory.GetSnapshot(new(pool)).CommittedBytes;
            Require(committed == 0, $"Pool {pool} retained {committed} bytes after release.");
        }

        Console.WriteLine("glTF checks passed; all resource memory charges returned.");
    }

    private static async Task ValidateCpuAsync(ResourceManager resources, string directory)
    {
        var key = new ResourceKey<CpuMesh>("models/cubes.gltf", "mesh/0");
        using ResourceLease<CpuMesh> mesh = await resources.AcquireAsync(key);
        Require(mesh.Value.Vertices.Memory.Length == 36, "Expected the cube's twelve triangles.");
        Require(mesh.Value.Material.Texture.Address.Id.Value == "textures/checker.png", "External texture URI did not resolve relative to the document.");
        using ResourceLease<CpuImage> image = await resources.AcquireAsync(new ResourceKey<CpuImage>("textures/checker.png"));
        Require(image.Value.Width == 64 && image.Value.Height == 64, "External PNG did not decode.");

        // Invalid external data must fail a transaction without replacing the existing generation.
        string path = Path.Combine(directory, "models", "cubes.bin");
        byte[] original = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path, original[..8]);
        try
        {
            await resources.ReloadAsync([new("models/cubes.bin")]);
            throw new InvalidOperationException("A truncated external buffer was published.");
        }
        catch (InvalidDataException)
        {
            // The failed candidate is discarded; the published generation remains usable.
        }

        using ResourceLease<CpuMesh> retained = await resources.AcquireAsync(key);
        Require(ReferenceEquals(mesh.Value, retained.Value), "Failed reload replaced the valid mesh.");
        await File.WriteAllBytesAsync(path, original);
        await ValidateDocumentFailuresAsync(resources, directory, key, mesh.Value);
        byte[] changed = (byte[])original.Clone();
        BinaryPrimitives.WriteSingleLittleEndian(changed, -0.5f);
        await File.WriteAllBytesAsync(path, changed);
        ResourceReloadResult updated = await resources.ReloadAsync([new("models/cubes.bin")]);
        Require(updated.Status == ResourceReloadStatus.Published, "External BIN changes did not rebuild the dependent mesh.");
        using (ResourceLease<CpuMesh> current = await resources.AcquireAsync(key))
        {
            Require(current.Value.Vertices.Memory.Span[0].Position.X == -0.5f && mesh.Value.Vertices.Memory.Span[0].Position.X == -1, "BIN reload did not isolate mesh generations.");
        }

        await File.WriteAllBytesAsync(path, original);
        await resources.ReloadAsync([new("models/cubes.bin")]);
        Console.WriteLine("CPU pipeline: external BIN/PNG resolved; BIN reload isolated generations; malformed buffers/documents preserved the published mesh.");
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
                await resources.ReloadAsync([new("models/cubes.gltf")]);
                throw new InvalidOperationException("An unsupported or out-of-range glTF document was published.");
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            {
                using ResourceLease<CpuMesh> retained = await resources.AcquireAsync(key);
                Require(ReferenceEquals(expected, retained.Value), "A rejected document replaced the published mesh.");
            }
        }

        await File.WriteAllTextAsync(path, original);
    }

    private static async Task RenderAndReloadAsync(ResourceManager resources, ResourceReloadService changes, IGraphicsDeviceService devices, string directory, string output)
    {
        var key = new ResourceKey<GpuScene>("models/cubes.gltf", "scene/0");
        using ResourceReference<GpuScene> owner = await resources.CreateReferenceAsync(key);
        using ResourceLease<GpuScene> original = await resources.AcquireAsync(key);
        Require(original.Value.Draws.Count == 2 && ReferenceEquals(original.Value.Draws[0].Mesh, original.Value.Draws[1].Mesh), "Scene instances did not share the uploaded mesh.");
        using GraphicsDeviceLease device = await devices.AcquireAsync();
        byte[] before = await SceneRenderer.RenderAsync(device.Device, original.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        PngFile.Write(output, 512, 512, before);
        Console.WriteLine($"Rendered external-texture glTF: {output}");

        TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        changes.Reloaded += (_, args) =>
        {
            initialized.TrySetResult();
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        changes.ReloadFailed += (_, args) =>
        {
            if (!args.WillRetry)
            {
                published.TrySetException(args.Error);
            }
        };
        changes.Start();
        try
        {
            await initialized.Task.WaitAsync(TimeSpan.FromSeconds(20));
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
            Require(resources.TryAcquire(owner.Handle, out ResourceLease<GpuScene>? current), "The logical scene handle was lost during texture reload.");
            using (current)
            {
                byte[] after = await SceneRenderer.RenderAsync(device.Device, current!.Value);
                Require(!after.AsSpan().SequenceEqual(before), "Changing only the external PNG did not alter the GPU scene.");
                byte[] old = await SceneRenderer.RenderAsync(device.Device, original.Value);
                Require(old.AsSpan().SequenceEqual(before), "Texture reload mutated the old leased GPU generation.");
                PngFile.Write(Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-reloaded.png"), 512, 512, after);
            }

            Console.WriteLine("Automatic PNG reload propagated image → GPU texture → GPU mesh → scene; the same handle resolved the new image and the old lease rendered unchanged.");
        }
        finally
        {
            await changes.StopAsync();
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
