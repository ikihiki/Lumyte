using System.Security.Cryptography;
using System.Text;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Supplies completed generic stage binaries; never compiles source at runtime.</summary>
public sealed class GenericGpuArtifactProvider2D : IStageArtifactProvider
{
    private readonly IReadOnlyDictionary<string, ShaderArtifact> _artifacts;

    /// <summary>Initializes a new instance of the <see cref="GenericGpuArtifactProvider2D"/> class.Captures completed artifacts by the exact generic resource suffix (for example generic-coverage-compute).</summary>
    /// <param name="artifacts">The immutable completed module inventory.</param>
    public GenericGpuArtifactProvider2D(IReadOnlyDictionary<string, ShaderArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var inventory = new Dictionary<string, ShaderArtifact>(artifacts);
        if (!inventory.ContainsKey("drawing-vertex"))
        {
            inventory.Add("drawing-vertex", DrawingShaders2D.Vertex);
        }

        if (!inventory.ContainsKey("drawing-fragment"))
        {
            inventory.Add("drawing-fragment", DrawingShaders2D.Fragment);
        }

        _artifacts = inventory;
    }

    /// <summary>Loads the explicit build-time embedded artifact inventory.</summary>
    /// <returns>The completed embedded module provider.</returns>
    public static GenericGpuArtifactProvider2D CreateEmbedded()
    {
        string[] names = ["generic-coverage-compute", "generic-paint-compute", "generic-clip-compute", "generic-vertex", "generic-compose-fragment", "generic-fused-fragment", "generic-mesh"];
        return new(names.ToDictionary(n => n, n => ShaderArtifact.LoadEmbedded(typeof(GenericGpuArtifactProvider2D).Assembly, $"Lumyte.Drawing2D.{n}.lshader")));
    }

    /// <inheritdoc/>
    public StageArtifactDescription Describe(StageArtifactKey key, IGraphicDevice device)
    {
        ShaderArtifact[] modules = Modules(key);
        if (key.ImplementationType == typeof(MeshEvaluateAndComposite2D))
        {
            DeviceCaps caps = device.Caps;
            if (!caps.Features.HasFlag(GraphicsFeatures.MeshShader) || caps.ShaderTarget == ShaderTarget.Wgsl || caps.MeshShaderLimits is not { } limits)
            {
                throw new NotSupportedException("Mesh shaders are required before acquiring native resources; Browser/Wgpu is unsupported.");
            }

            MeshShaderValidation.Validate(modules[0].GetTarget(caps.ShaderTarget), caps);
            MeshShaderValidation.Program(modules[0].GetTarget(caps.ShaderTarget), null, modules[1].GetTarget(caps.ShaderTarget));
            ShaderWorkgroupLimits workgroup = limits.Workgroup;
            if (workgroup.MaxGroupCountX == 0 || workgroup.MaxGroupCountY == 0 || workgroup.MaxGroupCountZ == 0 || workgroup.MaxTotalGroupCount == 0)
            {
                throw new NotSupportedException("The native mesh endpoint requires one dispatch group.");
            }
        }

        using var programs = new MemoryStream();
        using var abis = new MemoryStream();
        using var pw = new BinaryWriter(programs, Encoding.UTF8, true);
        using var aw = new BinaryWriter(abis, Encoding.UTF8, true);
        foreach (ShaderArtifact module in modules)
        {
            ShaderTargetData target = module.GetTarget(device.Caps.ShaderTarget);
            byte[] binary = module.GetBinary();
            pw.Write(binary.Length);
            pw.Write(binary);
            byte[] reflection = Encoding.UTF8.GetBytes(target.ReflectionJson);
            aw.Write(reflection.Length);
            aw.Write(reflection);
        }

        return new(key, device.Caps.ShaderTarget.ToString(), Convert.ToHexString(SHA256.HashData(abis.ToArray())), Convert.ToHexString(SHA256.HashData(programs.ToArray())));
    }

    /// <inheritdoc/>
    public StageArtifactLease Acquire(StageArtifactKey key, StageArtifactBuildContext context)
    {
        StageArtifactDescription description = Describe(key, context.Device);
        var owner = new NativeOwners();
        try
        {
            IGraphicsShader[] shaders = Modules(key).Select(a => owner.Add(context.Device.CreateShader(a))).ToArray();
            IGraphicsPipeline? graphics = null;
            IGraphicsComputePipeline? compute = null;
            IMeshPipeline? mesh = null;
            if (key.ImplementationType == typeof(MeshEvaluateAndComposite2D))
            {
                mesh = owner.Add(context.Device.CreateMeshPipeline(new() { MeshShader = shaders[0], FragmentShader = shaders[1] }));
            }
            else if (shaders.Length == 1)
            {
                compute = owner.Add(context.Device.CreateComputePipeline(new() { ComputeShader = shaders[0] }));
            }
            else
            {
                graphics = owner.Add(context.Device.CreateGraphicsPipeline(new() { VertexShader = shaders[0], FragmentShader = shaders[1] }));
            }

            IGraphicsSampler sampler = owner.Add(context.Device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge }));
            return new(description, context.Device, context.Generation, graphics, compute, mesh, sampler, owner);
        }
        catch (Exception primary)
        {
            context.CleanupAfterFailure(owner, primary);
            throw;
        }
    }

    private ShaderArtifact[] Modules(StageArtifactKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Version != 1 || key.Index != 0)
        {
            throw new NotSupportedException("Unknown generic stage artifact version or pass index.");
        }

        if (key == GraphCompositionArtifact2D.Key)
        {
            return [_artifacts["drawing-vertex"], _artifacts["drawing-fragment"]];
        }

        Type t = key.ImplementationType;
        string[] names = t == typeof(ComputeCoverage2D) ? ["generic-coverage-compute"] : t == typeof(ComputePaint2D) ? ["generic-paint-compute"] : t == typeof(ComputeClip2D) ? ["generic-clip-compute"] : t == typeof(RasterCompose2D) ? ["generic-vertex", "generic-compose-fragment"] : t == typeof(RasterEvaluateAndComposite2D) ? ["generic-vertex", "generic-fused-fragment"] : t == typeof(MeshEvaluateAndComposite2D) ? ["generic-mesh", "generic-fused-fragment"] : throw new NotSupportedException("Unknown generic stage implementation.");
        return names.Select(n => _artifacts.TryGetValue(n, out ShaderArtifact? a) ? a : throw new NotSupportedException($"Missing completed artifact: {n}.")).ToArray();
    }

    private sealed class NativeOwners : IDisposable
    {
        private readonly List<IDisposable> _items = [];

        public void Dispose()
        {
            List<Exception> errors = [];
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                try
                {
                    _items[i].Dispose();
                    _items.RemoveAt(i);
                }
                catch (Exception e)
                {
                    errors.Add(e);
                }
            }

            if (errors.Count != 0)
            {
                throw new AggregateException(errors);
            }
        }

        internal T Add<T>(T item)
            where T : IDisposable
        {
            _items.Add(item);
            return item;
        }
    }
}
