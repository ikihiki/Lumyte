namespace Lumyte.Graphics;

/// <summary>Available backend choices. Unsupported choices must fail explicitly.</summary>
public enum GraphicsBackend { Wgpu }

/// <summary>Composition root for the common graphics API.</summary>
public static class Graphics
{
    public static GraphicsDevice CreateDevice(GraphicsBackend backend = GraphicsBackend.Wgpu) => backend switch {
        GraphicsBackend.Wgpu => Wgpu.WgpuBackend.CreateDevice(),
        _ => throw new ArgumentOutOfRangeException(nameof(backend)),
    };
}
