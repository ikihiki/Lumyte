namespace Lumyte.Graphics;

/// <summary>Backend integration points for command-buffer implementations.</summary>
public static class GpuBackendCommands
{
    /// <summary>Validates the whole batch before ending any recording. Queues must serialize submission.</summary>
    public static T[] PrepareSubmission<T>(ReadOnlySpan<GpuCommandBuffer> commands, Func<T, bool> belongsToQueue)
        where T : class, IGpuCommandRecorder
    {
        if (commands.IsEmpty) { throw new ArgumentException("At least one command buffer is required.", nameof(commands)); }
        var seen = new HashSet<GpuCommandBuffer>();
        var recorders = new T[commands.Length];
        for (int index = 0; index < commands.Length; index++)
        {
            GpuCommandBuffer command = commands[index];
            ArgumentNullException.ThrowIfNull(command);
            if (!seen.Add(command)) { throw new ArgumentException("A command buffer cannot occur twice in a submission.", nameof(commands)); }
            command.ValidateSubmission();
            if (command.Recorder is not T recorder || !belongsToQueue(recorder))
            {
                throw new ArgumentException("Command buffer belongs to another backend or device.", nameof(commands));
            }
            recorders[index] = recorder;
        }
        foreach (GpuCommandBuffer command in commands)
        {
            if (command.State == GpuCommandBufferState.Recording) { command.Finish(); }
        }
        return recorders;
    }

    /// <summary>Transfers ownership to the queue before native execution can begin.</summary>
    public static void MarkSubmitted(ReadOnlySpan<GpuCommandBuffer> commands)
    {
        foreach (GpuCommandBuffer command in commands) { command.MarkSubmitted(); }
    }

    public static GpuCommandBuffer CreateCommandBuffer(IGpuCommandRecorder recorder)
        => new(recorder);

    public static IGpuCommandRecorder Finish(GpuCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        return commands.Finish();
    }

    public static IGpuCommandRecorder GetRecorder(GpuCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        return commands.Recorder;
    }

    public static uint BytesPerPixel(GpuFormat format) => GpuFormatInfo.BytesPerPixel(format);

    public static bool HasDepth(GpuFormat format) => GpuFormatInfo.HasDepth(format);

    public static bool HasStencil(GpuFormat format) => GpuFormatInfo.HasStencil(format);
}
