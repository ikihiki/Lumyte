using Lumyte.Graphics.Gpu.Benchmarks;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Vulkan;

if (args.SequenceEqual(new[] { "--smoke" }))
{
    SuiteSmoke.Verify();
    Console.WriteLine("Image and transfer CPU fixture checks passed; no GPU correctness claimed.");
    return 0;
}

SuiteOptions options;
try
{
    options = SuiteOptions.Parse(args);
}
catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}

Directory.CreateDirectory(options.Output);
VulkanDevice? device = null;
try
{
    device = VulkanDevice.Create();
    await SuiteRunner.RunAsync(device, options);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
finally
{
    if (PublicRetainedApiProbe.QuarantinedObjects.Count == 0)
    {
        device?.Dispose();
    }
    else if (device is not null)
    {
        PublicRetainedApiProbe.QuarantinedObjects.Add(device);
    }
}
