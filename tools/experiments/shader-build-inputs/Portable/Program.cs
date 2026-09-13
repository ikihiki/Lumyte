using System.Runtime.InteropServices;
using Lumyte.Graphics.Portable.Shaders;

PortableShaderPackage package = BuildProbe.Portable.ProbePackage.Create();
if (BuildProbe.Portable.ProbeGroup0Resources.AbiHash != package.AbiHash ||
    Marshal.SizeOf<BuildProbe.Portable.ProbeRootData>() != package.RootLayout!.Size)
{
    throw new InvalidOperationException("Generated inputs do not match the Portable package.");
}
Console.WriteLine($"Portable: root {package.RootLayout.Size} bytes; generated binding ABI matches.");
