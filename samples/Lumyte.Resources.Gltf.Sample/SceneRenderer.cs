using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Gltf.Sample;

internal static class SceneRenderer
{
    private const uint Size = 512;

    internal static async Task<byte[]> RenderAsync(IGraphicDevice device, GpuScene scene)
    {
        using IGraphicsTexture target = device.CreateTexture(new() { Width = Size, Height = Size, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        using IGraphicsTexture depth = device.CreateTexture(new() { Width = Size, Height = Size, Format = TextureFormat.Depth32Float, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView depthView = depth.CreateView();
        uint alignment = device.GetTextureCopyLayout(target.Format).BytesPerRowAlignment;
        uint pitch = checked((((Size * 4) + alignment - 1) / alignment) * alignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * Size, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var colorWrite = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, Before = default, AfterState = TextureState.ColorAttachment, After = colorWrite });
        commands.Barrier(new TextureBarrierDesc { Texture = depth, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, Before = default, AfterState = TextureState.DepthStencilAttachment, After = new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite) });
        IRenderEncoder render = commands.BeginRenderPass(new()
        {
            ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0.025f, 0.035f, 0.065f, 1) }],
            DepthStencilAttachment = new() { View = depthView },
        });
        render.SetRenderState(new()
        {
            ColorTargets = [new()],
            Rasterization = new() { Cull = CullMode.None },
            DepthStencil = new() { DepthTestEnable = true, DepthWriteEnable = true },
        });
        render.SetViewport(new(0, 0, Size, Size, 0, 1));
        render.SetScissor(new(0, 0, Size, Size));
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(0);
        Matrix4x4 camera = Matrix4x4.CreateLookAt(new(4, 3, 5), new(0, 0, 0), Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, 1, 0.1f, 100);
        foreach (SceneDraw draw in scene.Draws)
        {
            GpuMesh mesh = draw.Mesh;
            render.SetPipeline(mesh.Pipeline);
            render.SetArgumentTable(mesh.Table);
            var arguments = new DrawArguments(mesh.Vertices, draw.World * camera, mesh.Color, mesh.Texture, mesh.Sampler);
            render.SetArguments(in arguments);
            render.Draw(mesh.VertexCount);
        }

        render.End();
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = colorWrite, After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = Size, Height = Size }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = Size });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] padded = new byte[checked((int)readback.Count)];
        readback.CopyTo(padded);
        readback.Unmap();
        byte[] rgba = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
        {
            padded.AsSpan(y * (int)pitch, (int)Size * 4).CopyTo(rgba.AsSpan(y * (int)Size * 4));
        }

        // A clear-only image cannot satisfy this check.
        int foreground = 0;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (!rgba.AsSpan(i, 3).SequenceEqual(rgba.AsSpan(0, 3)))
            {
                foreground++;
            }
        }

        if (foreground < 5000 || rgba.Chunk(4).Select(pixel => (pixel[0], pixel[1], pixel[2])).Distinct().Count() < 12)
        {
            throw new InvalidOperationException("The textured 3D scene did not reach the render target.");
        }

        return rgba;
    }
}
