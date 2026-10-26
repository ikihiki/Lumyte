using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing;
using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics.Drawing;

/// <summary>Reconstructs an initialized mesh from tracked CPU streams on the selected physical device epoch.</summary>
public sealed class ModelMeshDefinition : GraphicsResourceDefinition<ModelMesh>
{
    private readonly ResourceKey<ModelMeshData> _source;
    private readonly ResourceKey<ShaderArtifact>? _vertexArtifact;

    /// <summary>Initializes a new instance of the <see cref="ModelMeshDefinition"/> class using the embedded shader ABI.</summary>
    /// <param name="source">The tracked format-independent CPU streams.</param>
    public ModelMeshDefinition(ResourceKey<ModelMeshData> source)
    {
        _source = source;
    }

    /// <summary>Initializes a new instance of the <see cref="ModelMeshDefinition"/> class using a tracked shader ABI.</summary>
    /// <param name="source">The tracked format-independent CPU streams.</param>
    /// <param name="vertexArtifact">The matching vertex artifact, tracked for HotReload.</param>
    public ModelMeshDefinition(ResourceKey<ModelMeshData> source, ResourceKey<ShaderArtifact> vertexArtifact)
    {
        _source = source;
        _vertexArtifact = vertexArtifact;
    }

    /// <inheritdoc/>
    public override async ValueTask<ModelMesh> CreateAsync(IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        ModelMeshData data = await context.RequireAsync(_source, ResourceDependencyLifetime.Build, cancellationToken);
        if (context.Runtime is not GraphicsResourceRuntime runtime)
        {
            throw new InvalidOperationException("Mesh initialization requires a graphics runtime.");
        }

        GraphicsDeviceLease epoch = await runtime.AcquireAsync(context, ResourceDependencyLifetime.Build, cancellationToken);
        if (!ReferenceEquals(epoch.Device, device))
        {
            throw new InvalidOperationException("The mesh upload must use the selected physical device.");
        }

        var upload = new UploadOwner(device, runtime, epoch.State.Epoch);
        context.RegisterCleanup(upload.DisposeAsync, ResourceDependencyLifetime.Build);
        cancellationToken.ThrowIfCancellationRequested();
        ShaderArtifact artifact = _vertexArtifact is { } key
            ? await context.RequireAsync(key, ResourceDependencyLifetime.Build, cancellationToken)
            : DrawingShaders.LoadModelVertex();
        upload.Mesh = data.IndexFormat == IndexFormat.Uint16
            ? await ModelMesh.CreatePackedAsync(device, data.Vertices, data.Indices16, artifact, partial => upload.Partial = partial)
            : await ModelMesh.CreatePackedAsync(device, data.Vertices, data.Indices32, artifact, partial => upload.Partial = partial);
        upload.Commands = device.CreateCommandBuffer(new() { Label = "model-upload" });
        upload.Mesh.RecordInitialUpload(upload.Commands);
        upload.Commands.Finish();
        upload.Submission = device.Queue.Submit([upload.Commands]);

        // Once submitted, retain all resources until actual completion or epoch retirement.
        await upload.Submission.WaitAsync();
        cancellationToken.ThrowIfCancellationRequested();
        upload.ReleaseCommands();
        ModelMesh mesh = upload.Mesh;
        upload.Mesh = null;
        return mesh;
    }

    private sealed class UploadOwner(IGraphicDevice device, GraphicsResourceRuntime runtime, ulong epoch)
    {
        public ModelMesh? Mesh { get; set; }

        public IDisposable? Partial { get; set; }

        public IGraphicsCommandBuffer? Commands { get; set; }

        public IGraphicsSubmission? Submission { get; set; }

        public void ReleaseCommands()
        {
            Submission?.Dispose();
            Submission = null;
            Commands?.Dispose();
            Commands = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (Mesh is null && Partial is null && Commands is null && Submission is null)
            {
                return;
            }

            if (device.Status == GraphicsDeviceStatus.Lost)
            {
                await runtime.WaitForRetirementAsync(epoch);
            }
            else if (Submission is not null)
            {
                await Submission.WaitAsync();
            }

            ReleaseCommands();
            Mesh?.Dispose();
            Mesh = null;
            Partial?.Dispose();
            Partial = null;
        }
    }
}
