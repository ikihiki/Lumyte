using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Memory;
using Lumyte.Resources.Graphics;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class MeshUploadStage([FromKeyedServices("main")] IGraphicsDeviceService devices) : IResourceTransform<CpuMesh, GpuMesh>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => output;

    public async ValueTask<GpuMesh> BuildAsync(CpuMesh input, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        GpuTexture texture = await context.RequireAsync(new ResourceKey<GpuTexture>(input.Material.TextureKey.Address), cancellationToken: cancellationToken);
        ShaderArtifact vertexArtifact = await context.RequireAsync(new ResourceKey<ShaderArtifact>("shaders/vertex"), ResourceDependencyLifetime.Build, cancellationToken);
        ShaderArtifact fragmentArtifact = await context.RequireAsync(new ResourceKey<ShaderArtifact>("shaders/fragment"), ResourceDependencyLifetime.Build, cancellationToken);
        GraphicsDeviceLease lease = await context.UseGraphicsAsync(devices, "graphics.main", cancellationToken: cancellationToken);
        IGraphicDevice device = lease.Device;
        var native = NativeResources.Attach(context, devices, lease);
        IGraphicsShader vertex = native.Own(device.CreateShader(vertexArtifact));
        IGraphicsShader fragment = native.Own(device.CreateShader(fragmentArtifact));
        IGraphicsPipeline pipeline = native.Own(device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment }));
        ulong count = (ulong)input.Vertices.Memory.Length;
        using IMemoryReservation reservation = await context.GetMemoryScope(ResourceDependencyLifetime.Resident).ReserveAsync(new() { Pool = new("gltf.gpu"), Bytes = checked(count * 64), Purpose = MemoryPurpose.Resident }, cancellationToken);
        IGraphicsShaderDataBuffer<GltfVertex> vertices = native.Own(device.CreateBuffer<GltfVertex>(vertexArtifact, count));
        _ = reservation.Commit(new(vertices.SizeInBytes, MemoryAccuracy.Exact));
        using IGraphicsShaderDataBuffer<GltfVertex> upload = device.CreateBuffer<GltfVertex>(vertexArtifact, count, MemoryPreference.Upload);
        await upload.MapAsync();
        upload.CopyFrom(input.Vertices.Memory.Span);
        upload.Unmap();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new ShaderDataBufferBarrierDesc<GltfVertex> { Buffer = upload.SliceElements(0, count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(upload.SliceElements(0, count), vertices.SliceElements(0, count));
        commands.Barrier(new ShaderDataBufferBarrierDesc<GltfVertex> { Buffer = vertices.SliceElements(0, count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.VertexShader, ResourceAccess.ShaderRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        IGraphicsSampler sampler = native.Own(device.CreateSampler(new()));
        IArgumentTable table = native.Own(device.CreateArgumentTable(new() { BufferCapacity = 1, TextureCapacity = 1, SamplerCapacity = 1 }));
        return new(pipeline, table, table.WriteBuffer(0, vertices.SliceElements(0, count)), (uint)count, input.Material.Color, table.WriteTexture(0, texture.View), table.WriteSampler(0, sampler));
    }

    public ValueTask ReleaseAsync(GpuMesh resource) => ValueTask.CompletedTask;
}
