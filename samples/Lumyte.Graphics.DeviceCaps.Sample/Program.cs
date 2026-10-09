using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;

if (args is ["wgpu"] or ["wgpu", "buffers"] or ["wgpu", "textures"] or ["wgpu", "samplers"] or ["wgpu", "arguments"])
{
    using var device = WgpuDevice.Create();
    Console.WriteLine(CapsDisplay.Describe(device));
    if (args.Length == 2)
    {
        Console.WriteLine(args[1] == "buffers" ? await BufferExercise.RunAsync(device) : args[1] == "textures" ? TextureExercise.Run(device) : args[1] == "samplers" ? SamplerExercise.Run(device) : ArgumentTableExercise.Run(device));
    }
}
else if (args is ["vulkan"] or ["vulkan", "buffers"] or ["vulkan", "textures"] or ["vulkan", "samplers"] or ["vulkan", "arguments"])
{
    using var device = VulkanDevice.Create();
    Console.WriteLine(CapsDisplay.Describe(device));
    if (args.Length == 2)
    {
        Console.WriteLine(args[1] == "buffers" ? await BufferExercise.RunAsync(device) : args[1] == "textures" ? TextureExercise.Run(device) : args[1] == "samplers" ? SamplerExercise.Run(device) : ArgumentTableExercise.Run(device));
    }
}
else
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -- wgpu|vulkan [buffers|textures|samplers|arguments]");
    return 1;
}

return 0;
