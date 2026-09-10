namespace Lumyte.Graphics.Native;

/// <summary>An unambiguous compute, vertex raster, or mesh raster program; it does not compile or inspect shader code.</summary>
public sealed class NativeGpuShaderProgram
{
    public NativeGpuShaderProgram(params NativeGpuShaderCode[] shaders)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        foreach (NativeGpuShaderCode shader in shaders)
        {
            if (shader is null) { throw InvalidProgram(); }
            switch (shader.Stage)
            {
                case GpuShaderStage.Vertex when Vertex is null: Vertex = shader; break;
                case GpuShaderStage.Pixel when Pixel is null: Pixel = shader; break;
                case GpuShaderStage.Compute when Compute is null: Compute = shader; break;
                case GpuShaderStage.Mesh when Mesh is null: Mesh = shader; break;
                case GpuShaderStage.Amplification when Amplification is null: Amplification = shader; break;
                default: throw InvalidProgram();
            }
        }

        bool compute = Compute is not null && Vertex is null && Pixel is null && Mesh is null && Amplification is null;
        bool vertex = Vertex is not null && Compute is null && Mesh is null && Amplification is null;
        bool mesh = Mesh is not null && Compute is null && Vertex is null;
        if (!compute && !vertex && !mesh) { throw InvalidProgram(); }

        static ArgumentException InvalidProgram() => new(
            "A program must contain one compute shader, one vertex with optional pixel, or one mesh with optional amplification and pixel; stages cannot repeat.", nameof(shaders));
    }

    public NativeGpuShaderCode? Vertex { get; }
    public NativeGpuShaderCode? Pixel { get; }
    public NativeGpuShaderCode? Compute { get; }
    public NativeGpuShaderCode? Mesh { get; }
    public NativeGpuShaderCode? Amplification { get; }
}
