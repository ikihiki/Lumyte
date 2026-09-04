namespace Lumyte.Graphics.TwoD;

internal static class StandardShaders
{
    private const string ResourceName = "Lumyte.Graphics.TwoD.Shaders.PhaseOne.lshp";

    public static GpuShaderPackage Load()
    {
        using Stream stream = typeof(StandardShaders).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The standard 2D shader package is not embedded.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return GpuShaderPackage.Read(memory.ToArray());
    }
}
