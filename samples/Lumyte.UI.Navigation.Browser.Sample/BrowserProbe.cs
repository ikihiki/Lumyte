using System.Runtime.InteropServices.JavaScript;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Automation;

namespace Lumyte.UI.Navigation.Browser.Sample;

/// <summary>Exposes the sample's automation PNG for the browser smoke test.</summary>
public static partial class BrowserProbe
{
    internal static UiApplicationWindowContext? Context { get; set; }

    internal static UiApplication? Application { get; set; }

    /// <summary>Gets the owner state used by the browser smoke test.</summary>
    /// <returns>The lifecycle and target state.</returns>
    [JSExport]
    public static string GetState() => $"{Application?.State}; {Application?.Failure}";

    /// <summary>Tests that the first frame has been presented before the screenshot request.</summary>
    /// <returns>Whether the sample root is visible in committed geometry.</returns>
    [JSExport]
    public static bool IsPresented() => Application?.State == UiApplicationState.Running && Context is { } context && context.Windows.TryGetSnapshot(context.Window, out Lumyte.UI.Windowing.UiWindowSnapshot? snapshot) && snapshot is { IsVisible: true };

    /// <summary>Gets the currently published in-app route.</summary>
    /// <returns>The route, or an empty string before commit.</returns>
    [JSExport]
    public static string GetRoute() => Context?.View.CaptureNavigationSnapshot().Current?.Route ?? string.Empty;

    /// <summary>Gets the stable window and changing surface identities.</summary>
    /// <returns>The current publication identities.</returns>
    [JSExport]
    public static string GetIdentities() => Context is { } context ? $"{context.Window};{context.View.CaptureNavigationSnapshot().Current?.SurfaceId}" : string.Empty;

    /// <summary>Captures the actual application frame through its public automation host.</summary>
    /// <returns>The PNG encoded for the test bridge.</returns>
    [JSExport]
    public static async Task<string> CapturePngAsync()
    {
        var page = new UiAutomationPage(Context?.Automation ?? throw new InvalidOperationException("The sample has not opened its root."), new() { Timeout = TimeSpan.FromSeconds(30) });
        return Convert.ToBase64String(await page.ScreenshotAsync());
    }
}
