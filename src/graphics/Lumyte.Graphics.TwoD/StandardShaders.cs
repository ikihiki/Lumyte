namespace Lumyte.Graphics.TwoD;

internal static class StandardShaders
{
    public static GpuShaderPackage LoadPhaseOne()
        => Load("Lumyte.Graphics.TwoD.Shaders.PhaseOne.lshp");

    public static GpuShaderPackage LoadPhaseTwo()
        => Load("Lumyte.Graphics.TwoD.Shaders.PhaseTwo.lshp");

    private static GpuShaderPackage Load(string resourceName)
    {
        using Stream stream = typeof(StandardShaders).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The standard 2D shader package '{resourceName}' is not embedded.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return GpuShaderPackage.Read(memory.ToArray());
    }
}
