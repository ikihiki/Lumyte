using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserQueue(BrowserDevice owner) : IGraphicsQueue
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

        var commands = new BrowserCommandBuffer[commandBuffers.Count];
        for (int i = 0; i < commandBuffers.Count; i++)
        {
            if (commandBuffers[i] is not BrowserCommandBuffer buffer || !ReferenceEquals(buffer.Owner, owner))
            {
                throw new ArgumentException("Command buffer belongs to another device.");
            }

            buffer.ValidateSubmit();
            commands[i] = buffer;
        }

        using JSObject list = BrowserInterop.CreateCommandList();
        foreach (BrowserCommandBuffer command in commands)
        {
            BrowserInterop.AddCommand(list, command.Native);
        }

        JSObject handle = BrowserInterop.SubmitCommands(owner.Handle, list);
        foreach (BrowserCommandBuffer command in commands)
        {
            command.MarkSubmitted();
        }

        return new BrowserSubmission(commands, handle);
    }
}
