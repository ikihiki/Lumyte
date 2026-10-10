using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuQueue(WgpuDevice owner) : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => Submit(new QueueSubmitDesc { CommandBuffers = commandBuffers });

    public IGraphicsSubmission Submit(QueueSubmitDesc desc)
    {
        owner.ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.CommandBuffers);
        ArgumentNullException.ThrowIfNull(desc.WaitSemaphores);
        ArgumentNullException.ThrowIfNull(desc.SignalSemaphores);
        IReadOnlyList<IGraphicsCommandBuffer> commandBuffers = desc.CommandBuffers;
        if (commandBuffers.Count == 0 && desc.WaitSemaphores.Count == 0 && desc.SignalSemaphores.Count == 0)
        {
            throw new ArgumentException("A submission must contain commands, waits or signals.");
        }

        var commands = new WgpuCommandBuffer[commandBuffers.Count];
        nint[] handles = new nint[commandBuffers.Count];
        for (int i = 0; i < commandBuffers.Count; i++)
        {
            if (commandBuffers[i] is not WgpuCommandBuffer buffer || !ReferenceEquals(buffer.Owner, owner))
            {
                throw new ArgumentException("Command buffer belongs to another device.");
            }

            buffer.ValidateSubmit();
            commands[i] = buffer;
            handles[i] = (nint)buffer.Native;
        }

        fixed (nint* data = handles)
        {
            WGPU.wgpuQueueSubmit(owner.NativeDevice.Queue.Handle, (nuint)handles.Length, (WGPUCommandBufferImpl**)data);
        }

        foreach (WgpuCommandBuffer buffer in commands)
        {
            buffer.MarkSubmitted();
        }

        return new WgpuSubmission(owner, commands);
    }
}
