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
        _report = CapsDisplay.Describe(device) + "\n" + await BufferExercise.RunAsync(device);
        Console.WriteLine(_report);
    }
}
