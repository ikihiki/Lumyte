using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuQueue(WgpuDevice owner) : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => SubmitCore(commandBuffers, null);

    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers, IGraphicsSurfaceFrame frame)
    {
        if (frame is not WgpuSurfaceFrame acquired || !ReferenceEquals(acquired.Owner, owner))
        {
            throw new ArgumentException("Presentation frame belongs to another device.", nameof(frame));
        }

        acquired.Lifetime.ValidateRecording();
        return SubmitCore(commandBuffers, acquired);
    }

    private IGraphicsSubmission SubmitCore(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers, WgpuSurfaceFrame? frame)
    {
        owner.ValidateAlive();
        ArgumentNullException.ThrowIfNull(commandBuffers);
        IGraphicsCommandBuffer[] snapshot = commandBuffers.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("Empty submission.");
        }

        var commands = new WgpuCommandBuffer[snapshot.Length];
        nint[] handles = new nint[snapshot.Length];
        HashSet<WgpuCommandBuffer> seen = [];
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i] is not WgpuCommandBuffer buffer || !ReferenceEquals(buffer.Owner, owner) || !seen.Add(buffer))
            {
                throw new ArgumentException("Invalid device or duplicate command buffer.");
            }

            buffer.ValidateSubmit();
            commands[i] = buffer;
            handles[i] = (nint)buffer.Native;
        }

        TextureState? finalSurfaceState = null;
        foreach (WgpuCommandBuffer command in commands)
        {
            finalSurfaceState = command.ValidateSurfaceSubmission(frame?.Lifetime) ?? finalSurfaceState;
        }

        if (frame != null && finalSurfaceState != TextureState.Present)
        {
            throw new InvalidOperationException("A frame submission must use its image and end in explicit Present state.");
        }

        ShaderDataTransferState.ValidateSubmission(commands.Select(c => c.ShaderDataTransfers));

        fixed (nint* data = handles)
        {
            WGPU.wgpuQueueSubmit(owner.NativeDevice.Queue.Handle, (nuint)handles.Length, (WGPUCommandBufferImpl**)data);
        }

        foreach (WgpuCommandBuffer buffer in commands)
        {
            buffer.MarkSubmitted();
        }

        var submission = new WgpuSubmission(owner, commands);
        owner.RetainSubmission();
        frame?.Lifetime.MarkSubmitted(submission);
        return submission;
    }
}
