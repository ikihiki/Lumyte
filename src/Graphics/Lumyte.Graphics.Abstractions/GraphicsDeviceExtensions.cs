namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides explicit names for device allocations with distinct layout contracts.</summary>
public static class GraphicsDeviceExtensions
{
    /// <summary>Creates shader ABI storage or upload staging without copying or submitting data.</summary>
    /// <typeparam name="T">The application shader data type.</typeparam>
    /// <param name="device">The borrowed backend device.</param>
    /// <param name="artifact">The shader artifact defining the layout.</param>
    /// <param name="count">The positive logical element count.</param>
    /// <param name="memory">Automatic for GPU storage or Upload for staging.</param>
    /// <returns>The caller-owned allocation.</returns>
    public static IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(this IGraphicDevice device, ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData
    {
        ArgumentNullException.ThrowIfNull(device);
        return device.CreateBuffer<T>(artifact, count, memory);
    }
}
