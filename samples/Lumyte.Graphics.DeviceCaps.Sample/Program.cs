using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;

if (args is ["wgpu"] or ["wgpu", "buffers"] or ["wgpu", "textures"] or ["wgpu", "samplers"])
{
    using var device = WgpuDevice.Create();
    Console.WriteLine(CapsDisplay.Describe(device));
    if (args.Length == 2)
    {
        Console.WriteLine(args[1] == "buffers" ? await BufferExercise.RunAsync(device) : args[1] == "textures" ? TextureExercise.Run(device) : SamplerExercise.Run(device));
    }
}
else if (args is ["vulkan"] or ["vulkan", "buffers"] or ["vulkan", "textures"] or ["vulkan", "samplers"])
{
    using var device = VulkanDevice.Create();
    Console.WriteLine(CapsDisplay.Describe(device));
    if (args.Length == 2)
    {
        Console.WriteLine(args[1] == "buffers" ? await BufferExercise.RunAsync(device) : args[1] == "textures" ? TextureExercise.Run(device) : SamplerExercise.Run(device));
    }
}
else
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -- wgpu|vulkan [buffers|textures|samplers]");
    return 1;
}

return 0;
