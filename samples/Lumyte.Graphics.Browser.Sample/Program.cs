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
        Console.WriteLine("Creating browser graphics device.");
        using BrowserDevice device = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
        List<string> reports = [];
        Record(CapsDisplay.Describe(device), reports);
        Record(await BufferExercise.RunAsync(device), reports);
        Record(TextureExercise.Run(device), reports);
        Record(SamplerExercise.Run(device), reports);
        Record(ArgumentTableExercise.Run(device), reports);
        Record(ShaderExercise.Run(device), reports);
        Record(await CommandExercise.RunAsync(device), reports);
        Record(await PipelineExercise.RunAsync(device), reports);
        Record(await AdvancedCommandExercise.RunAsync(device), reports);
        Record(await ShaderBindingExercise.RunAsync(device), reports);
        using BrowserDevice foreign = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
        ArgumentTableExercise.CheckForeignDevice(device, foreign);
        PipelineExercise.CheckForeignDevice(device, foreign);
        _report = string.Join("\n", reports);
    }

    private static void Record(string report, List<string> reports)
    {
        reports.Add(report);
        Console.WriteLine(report);
    }
}
