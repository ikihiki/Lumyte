using Lumyte.Graphics;

namespace Lumyte.Graphics.Library;

public static class StandardDrawShaders
{
    private const string ResourceName = "Lumyte.Graphics.Library.Shaders.AddDraw.lshp";

    public static GpuShaderPackage Load()
    {
        using Stream stream = typeof(StandardDrawShaders).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The standard AddDraw shader package is not embedded.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return GpuShaderPackage.Read(memory.ToArray());
    }
}
