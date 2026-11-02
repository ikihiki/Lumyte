using System.Runtime.InteropServices.JavaScript;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Lumyte.UI.Automation;

namespace Lumyte.UI.Application.Browser.Sample;

/// <summary>Exposes the sample's automation PNG for the browser smoke test.</summary>
public static partial class BrowserProbe
{
    internal static ManualClock Clock { get; } = new();

    internal static Signal<float?> Target { get; } = Signal.Create<float?>(160);

    internal static UiAnimated<float?>? Width { get; set; }

    internal static int Completed { get; set; }

    internal static UiApplicationWindowContext? Context { get; set; }

    internal static UiApplication? Application { get; set; }

    /// <summary>Gets the current animation value without evaluating time.</summary>
    /// <returns>The applied logical width.</returns>
    [JSExport]
    public static float GetWidth() => Width?.Peek() ?? 0;

    /// <summary>Gets the number of delivered final value notifications.</summary>
    /// <returns>The count.</returns>
    [JSExport]
    public static int GetCompletions() => Completed;

    /// <summary>Requests expansion through the independent model.</summary>
    /// <returns>The queued owner work.</returns>
    [JSExport]
    public static async Task ExpandAsync() => await Application!.Dispatcher.InvokeAsync(() => Target.Value = 240);

    /// <summary>Moves the borrowed test clock without manually advancing the surface.</summary>
    /// <param name="seconds">The positive elapsed time.</param>
    /// <returns>The queued owner work.</returns>
    [JSExport]
    public static async Task AdvanceAsync(double seconds) => await Application!.Dispatcher.InvokeAsync(() => Clock.Advance(Duration.FromSeconds(seconds)));

    /// <summary>Gets the owner state used by the browser smoke test.</summary>
    /// <returns>The lifecycle and target state.</returns>
    [JSExport]
    public static string GetState() => $"{Application?.State}; {Application?.Failure}";

    /// <summary>Tests that the first frame has been presented before the screenshot request.</summary>
    /// <returns>Whether the sample root is visible in committed geometry.</returns>
    [JSExport]
    public static bool IsPresented() => Application?.State == UiApplicationState.Running && Context is { } context && context.Windows.TryGetSnapshot(context.Window, out Lumyte.UI.Windowing.UiWindowSnapshot? snapshot) && snapshot is { IsVisible: true };

    /// <summary>Captures the actual application frame through its public automation host.</summary>
    /// <returns>The PNG encoded for the test bridge.</returns>
    [JSExport]
    public static async Task<string> CapturePngAsync()
    {
        var page = new UiAutomationPage(Context?.Automation ?? throw new InvalidOperationException("The sample has not opened its root."), new() { Timeout = TimeSpan.FromSeconds(30) });
        return Convert.ToBase64String(await page.ScreenshotAsync());
    }
}
