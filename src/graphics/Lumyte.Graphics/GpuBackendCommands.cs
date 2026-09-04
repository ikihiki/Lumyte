namespace Lumyte.Graphics;

/// <summary>Backend integration points for command-buffer implementations.</summary>
public static class GpuBackendCommands
{
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
