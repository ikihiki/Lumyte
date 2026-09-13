using System.Runtime.InteropServices;
using Lumyte.Graphics.Native.Shaders;

NativeShaderPackage package = BuildProbe.Native.ShaderPackage.Create();
string[] generatedHashes = [BuildProbe.Native.DirectX12.RootResources.AbiHash, BuildProbe.Native.Vulkan.RootResources.AbiHash];
int[] generatedSizes = [Marshal.SizeOf<BuildProbe.Native.DirectX12.Root>(), Marshal.SizeOf<BuildProbe.Native.Vulkan.Root>()];
for (int index = 0; index < package.Artifacts.Count; index++)
{
    NativeShaderArtifact artifact = package.Artifacts[index];
    if (generatedHashes[index] != artifact.AbiHash || generatedSizes[index] != artifact.RootLayout.Size)
    {
        throw new InvalidOperationException($"Generated inputs do not match {artifact.Target}.");
    }
    Console.WriteLine($"{artifact.Target}: root {generatedSizes[index]} bytes; generated resource ABI matches.");
}
