using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lumyte.Graphics.Abstractions;
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
        Record(await SemaphoreExercise.RunAsync(device), reports);
        Record(await PipelineExercise.RunAsync(device), reports);
        Record(await AdvancedCommandExercise.RunAsync(device), reports);
        Record(await ShaderBindingExercise.RunAsync(device), reports);
        using JSObject context = JSHost.GlobalThis.GetPropertyAsJSObject("lumyteSurfaceContext") ?? throw new InvalidOperationException("Test bootstrap supplied no GPUCanvasContext.");
        using JSObject secondContext = JSHost.GlobalThis.GetPropertyAsJSObject("lumyteSecondSurfaceContext") ?? throw new InvalidOperationException("Test bootstrap supplied no second GPUCanvasContext.");
        using JSObject invalidContext = JSHost.GlobalThis.GetPropertyAsJSObject("lumyteInvalidSurfaceContext") ?? throw new InvalidOperationException("Test bootstrap supplied no invalid context.");
        try
        {
            device.CreateSurfaces([context, invalidContext]);
            throw new InvalidOperationException("Invalid context collection was accepted.");
        }
        catch (JSException)
        {
            // Subsequent creation using the first context verifies cleanup of the partially created collection.
        }

        IReadOnlyList<IGraphicsSurface> surfaces = device.CreateSurfaces([context, secondContext]);
        using IGraphicsSurface surface = surfaces[0];
        using IGraphicsSurface secondSurface = surfaces[1];
        try
        {
            Record(await SurfaceExercise.RunAsync(device, surface), reports);
            Record(await MultiSurfaceExercise.RunAsync(device, surface, secondSurface), reports);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            throw;
        }

        using BrowserDevice foreign = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
        SemaphoreExercise.CheckForeignDevice(device, foreign);
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
