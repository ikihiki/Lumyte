using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lumyte.Graphics.Samples;

namespace Lumyte.Graphics.Browser.Sample;

[SupportedOSPlatform("browser")]
internal static partial class Program
{
    private static string _report = string.Empty;

    [JSExport]
    internal static string GetReport() => _report;

    private static async Task Main()
    {
        using BrowserDevice device = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
        string report = CapsDisplay.Describe(device) + "\n" + await BufferExercise.RunAsync(device) + "\n" + TextureExercise.Run(device) + "\n" + SamplerExercise.Run(device) + "\n" + ArgumentTableExercise.Run(device) + "\n" + ShaderExercise.Run(device) + "\n" + await CommandExercise.RunAsync(device) + "\n" + await PipelineExercise.RunAsync(device) + "\n" + await ShaderBindingExercise.RunAsync(device);
        using BrowserDevice foreign = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
        ArgumentTableExercise.CheckForeignDevice(device, foreign);
        PipelineExercise.CheckForeignDevice(device, foreign);
        _report = report;
        Console.WriteLine(_report);
    }
}
