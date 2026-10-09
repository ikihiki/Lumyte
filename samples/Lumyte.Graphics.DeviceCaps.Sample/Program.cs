using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;

if (args is ["wgpu"])
{
    using var device = WgpuDevice.Create();
    Console.WriteLine(CapsDisplay.Describe(device));
}
else if (args is ["vulkan"])
{
    using var device = VulkanDevice.Create();
    Console.WriteLine(CapsDisplay.Describe(device));
}
else
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -- wgpu|vulkan");
    return 1;
}

return 0;
