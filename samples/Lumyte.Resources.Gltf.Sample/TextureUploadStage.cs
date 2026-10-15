using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Memory;
using Lumyte.Resources.Graphics;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class TextureUploadStage([FromKeyedServices("main")] IGraphicsDeviceService devices) : IResourceTransform<CpuImage, GpuTexture>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => output;

    public async ValueTask<GpuTexture> BuildAsync(CpuImage input, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        GraphicsDeviceLease lease = await context.UseGraphicsAsync(devices, "graphics.main", cancellationToken: cancellationToken);
        IGraphicDevice device = lease.Device;
        var native = NativeResources.Attach(context, devices, lease);
        ulong bytes = checked((ulong)input.Width * (ulong)input.Height * 4);
        IMemoryScope memory = context.GetMemoryScope(ResourceDependencyLifetime.Resident);
        using IMemoryReservation reservation = await memory.ReserveAsync(new() { Pool = new("gltf.gpu"), Bytes = bytes, Purpose = MemoryPurpose.Resident }, cancellationToken);
        IGraphicsTexture texture = native.Own(device.CreateTexture(new() { Width = (uint)input.Width, Height = (uint)input.Height, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.CopyDestination | TextureUsage.Sampled }));
        _ = reservation.Commit(new(bytes, MemoryAccuracy.Estimate));
        IGraphicsTextureView view = native.Own(texture.CreateView());
        uint alignment = device.GetTextureCopyLayout(texture.Format).BytesPerRowAlignment;
        uint row = checked((uint)input.Width * 4);
        uint pitch = checked(((row + alignment - 1) / alignment) * alignment);
        int size = checked((int)(pitch * (uint)input.Height));
        IMemoryBuffer<byte> packed = await context.GetMemoryScope(ResourceDependencyLifetime.Build).RentAsync<byte>(new("gltf.upload"), size, MemoryPurpose.Build, cancellationToken);
        packed.Memory.Span.Clear();
        for (int y = 0; y < input.Height; y++)
        {
            input.Pixels.Memory.Span.Slice(y * (int)row, (int)row).CopyTo(packed.Memory.Span.Slice(y * (int)pitch));
        }

        using IGraphicsBuffer<byte> upload = device.CreateBuffer<byte>(new() { Count = (ulong)size, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        await upload.MapAsync();
        upload.CopyFrom(packed.Memory.Span);
        upload.Unmap();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = upload.Slice(0, upload.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, Before = default, AfterState = TextureState.CopyDestination, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
        commands.CopyBufferToTexture(new() { Buffer = upload.Slice(0, upload.Count), BytesPerRow = pitch, RowsPerImage = (uint)input.Height }, new() { Texture = texture, Width = (uint)input.Width, Height = (uint)input.Height });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopyDestination, AfterState = TextureState.Sampled, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);

        // Do not cancel an in-flight GPU wait: staging cannot be freed before completion.
        await submission.WaitAsync();
        return new(texture, view);
    }

    public ValueTask ReleaseAsync(GpuTexture resource) => ValueTask.CompletedTask;
}
