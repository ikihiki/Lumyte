using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;

if (args is ["wgpu"] or ["wgpu", "buffers"])
{
    using var device = WgpuDevice.Create();
    Console.WriteLine(CapsDisplay.Describe(device));
    if (args.Length == 2)
    {
        Console.WriteLine(await BufferExercise.RunAsync(device));
    }
}
else if (args is ["vulkan"] or ["vulkan", "buffers"])
{
    using var device = VulkanDevice.Create();
    Console.WriteLine(CapsDisplay.Describe(device));
    if (args.Length == 2)
    {
        Console.WriteLine(await BufferExercise.RunAsync(device));
    }
}
else
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -- wgpu|vulkan [buffers]");
    return 1;
}

return 0;
