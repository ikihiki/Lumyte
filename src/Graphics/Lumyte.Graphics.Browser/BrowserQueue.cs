using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserQueue(BrowserDevice owner) : IGraphicsQueue
{
    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers) => SubmitCore(commandBuffers, null);

    public IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers, IGraphicsSurfaceFrame frame)
    {
        if (frame is not BrowserSurfaceFrame acquired || !ReferenceEquals(acquired.Owner, owner))
        {
            throw new ArgumentException("Presentation frame belongs to another device.", nameof(frame));
        }

        acquired.Lifetime.ValidateRecording();
        return SubmitCore(commandBuffers, acquired);
    }

    private IGraphicsSubmission SubmitCore(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers, BrowserSurfaceFrame? frame)
    {
        owner.ValidateAlive();
        ArgumentNullException.ThrowIfNull(commandBuffers);
        IGraphicsCommandBuffer[] snapshot = commandBuffers.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("Empty submission.");
        }

        var commands = new BrowserCommandBuffer[snapshot.Length];
        var seen = new HashSet<BrowserCommandBuffer>();
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i] is not BrowserCommandBuffer buffer || !ReferenceEquals(buffer.Owner, owner) || !seen.Add(buffer))
            {
                throw new ArgumentException("Invalid device or duplicate command buffer.");
            }

            buffer.ValidateSubmit();
            commands[i] = buffer;
        }

        TextureState? finalSurfaceState = null;
        foreach (BrowserCommandBuffer command in commands)
        {
            finalSurfaceState = command.ValidateSurfaceSubmission(frame?.Lifetime) ?? finalSurfaceState;
        }

        if (frame != null && finalSurfaceState != TextureState.Present)
        {
            throw new InvalidOperationException("A frame submission must use its image and end in explicit Present state.");
        }

        ShaderDataTransferState.ValidateSubmission(commands.Select(c => c.ShaderDataTransfers));

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

        var submission = new BrowserSubmission(owner, commands, handle);
        owner.RetainSubmission();
        frame?.Lifetime.MarkSubmitted(submission);
        return submission;
    }
}
