using System.Security.Cryptography;
using System.Text;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Owns a validated program and its dependency owner until all uses return.</summary>
public sealed class StageArtifactLease : IDisposable
{
    private readonly object _gate = new();
    private IDisposable? _owner;
    private int _uses;
    private bool _closing;

    /// <summary>Initializes a new instance of the <see cref="StageArtifactLease"/> class.</summary>
    /// <param name="description">The exact target, program and ABI identities.</param>
    /// <param name="device">The borrowed device.</param>
    /// <param name="generation">The device generation.</param>
    /// <param name="graphics">The optional graphics program.</param>
    /// <param name="compute">The optional compute program.</param>
    /// <param name="mesh">The optional mesh program.</param>
    /// <param name="sampler">The optional sampler.</param>
    /// <param name="owner">Dependencies transferred only on constructor success.</param>
    public StageArtifactLease(StageArtifactDescription description, IGraphicDevice device, GraphicsDeviceGeneration generation, IGraphicsPipeline? graphics, IGraphicsComputePipeline? compute, IMeshPipeline? mesh, IGraphicsSampler? sampler, IDisposable owner)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(owner);
        generation.ValidateDevice(device);
        if (device.Status != GraphicsDeviceStatus.Ready || description.BackendTarget != device.Caps.ShaderTarget.ToString())
        {
            throw new ArgumentException("Artifact target or device lifetime mismatch.", nameof(description));
        }

        if ((graphics is null ? 0 : 1) + (compute is null ? 0 : 1) + (mesh is null ? 0 : 1) != 1)
        {
            throw new ArgumentException("Exactly one program is required.");
        }

        var modules = new List<(IGraphicsShader Shader, ShaderStage Stage)>();
        if (graphics is not null)
        {
            modules.Add((graphics.Desc.VertexShader, ShaderStage.Vertex));
            if (graphics.Desc.FragmentShader is { } fragment)
            {
                modules.Add((fragment, ShaderStage.Fragment));
            }
        }

        if (compute is not null)
        {
            modules.Add((compute.Desc.ComputeShader, ShaderStage.Compute));
        }

        if (mesh is not null)
        {
            if (mesh.Desc.TaskShader is { } task)
            {
                modules.Add((task, ShaderStage.Task));
            }

            modules.Add((mesh.Desc.MeshShader, ShaderStage.Mesh));
            if (mesh.Desc.FragmentShader is { } fragment)
            {
                modules.Add((fragment, ShaderStage.Fragment));
            }
        }

        using var programBytes = new MemoryStream();
        using var abiBytes = new MemoryStream();
        using var programWriter = new BinaryWriter(programBytes, Encoding.UTF8, true);
        using var abiWriter = new BinaryWriter(abiBytes, Encoding.UTF8, true);
        foreach ((IGraphicsShader shader, ShaderStage stage) in modules)
        {
            ArgumentNullException.ThrowIfNull(shader);
            ShaderTargetData data = shader.Artifact.GetTarget(device.Caps.ShaderTarget);
            if (data.Stage != stage || string.IsNullOrWhiteSpace(data.EntryPoint))
            {
                throw new ArgumentException("Program descriptor has an incompatible shader stage.");
            }

            byte[] binary = shader.Artifact.GetBinary();
            programWriter.Write(binary.Length);
            programWriter.Write(binary);
            byte[] reflection = Encoding.UTF8.GetBytes(data.ReflectionJson);
            abiWriter.Write(reflection.Length);
            abiWriter.Write(reflection);
        }

        if (Convert.ToHexString(SHA256.HashData(programBytes.ToArray())) != description.ProgramHash ||
            Convert.ToHexString(SHA256.HashData(abiBytes.ToArray())) != description.ReflectedAbiHash)
        {
            throw new ArgumentException("Actual program descriptor differs from the declared program or reflected ABI.", nameof(description));
        }

        ValidateNative(graphics ?? (object?)compute ?? mesh!, device);
        if (sampler is not null)
        {
            ValidateNative(sampler, device);
        }

        Description = description;
        Graphics = graphics;
        Compute = compute;
        Mesh = mesh;
        Sampler = sampler;
        Device = device;
        Generation = generation;
        _owner = owner;
    }

    /// <summary>Gets the validated description.</summary>
    public StageArtifactDescription Description { get; }

    /// <summary>Gets the graphics program, if supplied.</summary>
    public IGraphicsPipeline? Graphics { get; }

    /// <summary>Gets the compute program, if supplied.</summary>
    public IGraphicsComputePipeline? Compute { get; }

    /// <summary>Gets the mesh program, if supplied.</summary>
    public IMeshPipeline? Mesh { get; }

    /// <summary>Gets the sampler, if supplied.</summary>
    public IGraphicsSampler? Sampler { get; }

    internal IGraphicDevice Device { get; }

    internal GraphicsDeviceGeneration Generation { get; }

    /// <summary>Stops new uses and releases dependencies after existing uses return; failures remain retryable.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _closing = true;
            if (_uses != 0)
            {
                throw new InvalidOperationException("Artifact still has active uses.");
            }

            _owner?.Dispose();
            _owner = null;
        }
    }

    internal void Validate()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_owner is null, this);
            Generation.ValidateDevice(Device);
            ValidateNative(Graphics ?? (object?)Compute ?? Mesh!, Device);
            if (Sampler is not null)
            {
                ValidateNative(Sampler, Device);
            }
        }
    }

    internal IDisposable AcquireUse()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing || _owner is null, this);
            _uses = checked(_uses + 1);
            return new Use(this);
        }
    }

    private static void ValidateNative(object resource, IGraphicDevice device)
    {
        if (resource is not IGraphicsNativeOrigin origin)
        {
            throw new NotSupportedException("Artifacts require actual backend resource-origin metadata.");
        }

        origin.ValidateOrigin();
        if (!ReferenceEquals(origin.Device, device))
        {
            throw new ArgumentException("The actual artifact program or sampler belongs to another native device.");
        }
    }

    private sealed class Use(StageArtifactLease parent) : IDisposable
    {
        private bool _returned;

        public void Dispose()
        {
            lock (parent._gate)
            {
                if (!_returned)
                {
                    parent._uses--;
                    _returned = true;
                }
            }
        }
    }
}
