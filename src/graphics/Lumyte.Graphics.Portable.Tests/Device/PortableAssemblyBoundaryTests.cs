namespace Lumyte.Graphics.Portable.Tests.Device;

public sealed class PortableAssemblyBoundaryTests
{
    [Fact]
    public void PortableContractsDoNotDependOnNativeOrBackendAssemblies()
    {
        string[] references = typeof(IPortableGpuBackend).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty).ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("Lumyte.Graphics.Native", StringComparison.Ordinal)
            || name is "Lumyte.Graphics.DirectX12" or "Lumyte.Graphics.Vulkan" or "Lumyte.Graphics.WebGPU");
    }
}
