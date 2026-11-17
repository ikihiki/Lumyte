using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Automation;

namespace Lumyte.Maps.Browser.Sample;

/// <summary>Exports observable adoption and actual host GPU capture for the bounded browser smoke.</summary>
public static partial class BrowserProbe
{
    internal static BrowserMapContent? Content { get; set; }

    internal static UiApplication? Application { get; set; }

    internal static UiApplicationWindowContext? Context { get; set; }

    /// <summary>Gets whether a decoded tile was actually adopted, rather than merely dispatching a request.</summary>
    /// <returns>Whether the session has published its tile.</returns>
    [JSExport]
    public static bool HasLoadedTile() => Content?.HasLoadedTile == true;

    /// <summary>Reads actual terminally adopted tiles after viewport demand changes.</summary>
    /// <returns>The published tile count.</returns>
    [JSExport]
    public static int GetPublishedTiles() => Content?.PublishedTiles ?? 0;

    /// <summary>Reads the camera after real DOM input has passed through the application host.</summary>
    /// <returns>Longitude, latitude and zoom using invariant culture.</returns>
    [JSExport]
    public static string GetCamera() => FormattableString.Invariant($"{Content!.Camera.Longitude},{Content.Camera.Latitude},{Content.Camera.Zoom}");

    /// <summary>Observes committed presentation geometry before injecting physical input into the host.</summary>
    /// <returns>Whether the normal public window hit-test can address the map's presented center.</returns>
    [JSExport]
    public static async Task<bool> HasPresentedInputAsync() => await Application!.Dispatcher.InvokeAsync(() =>
    {
        UiApplicationWindowContext context = Context!;
        return context.Windows.TryGetSnapshot(context.Window, out Lumyte.UI.Windowing.UiWindowSnapshot? snapshot) && snapshot is not null
            && context.Windows.HitTest(snapshot.PresentationRoot, snapshot.ClientSize / 2) is not null;
    });

    /// <summary>Gets current lifecycle diagnostics.</summary>
    /// <returns>Application state and failure.</returns>
    [JSExport]
    public static string GetState() => $"{Application?.State}; {Application?.Failure}";

    /// <summary>Records the mounted application map through both public UI drawing paths.</summary>
    /// <returns>The direct command count after checking retained recording and reuse.</returns>
    [JSExport]
    public static async Task<int> RecordUiAsync() => await Application!.Dispatcher.InvokeAsync(() =>
    {
        UiSurface surface = Context!.View.CurrentSurface!;
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        DrawList2D commands = canvas.End();
        using UiDrawingScene2D drawing = surface.CreateDrawingScene();
        drawing.Update();
        if (drawing.LastRecordingCount != 1 || commands.Count < 5)
        {
            throw new InvalidOperationException("The adopted map must record background, polygons and road strokes.");
        }

        drawing.Update();
        if (drawing.LastRecordingCount != 0)
        {
            throw new InvalidOperationException("The unchanged mounted map must reuse retained drawing.");
        }

        return commands.Count;
    });

    /// <summary>Captures the map through the application's automation host.</summary>
    /// <returns>Base64 GPU PNG.</returns>
    [JSExport]
    public static async Task<string> CapturePngAsync()
    {
        var page = new UiAutomationPage(Context?.Automation ?? throw new InvalidOperationException("The map has not initialized."), new() { Timeout = TimeSpan.FromSeconds(30) });
        return Convert.ToBase64String(await page.ScreenshotAsync());
    }

    /// <summary>Requests owner-queued stop without retaining an exported promise across runtime shutdown.</summary>
    [JSExport]
    public static void RequestStop() => Context!.RequestStop();
}
