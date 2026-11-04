using System.Runtime.InteropServices.JavaScript;
using Lumyte.Core.Time;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Automation;
using Lumyte.UI.Transitions.SampleSupport;

namespace Lumyte.UI.Transitions.Browser.Sample;

/// <summary>Provides test observations and a borrowed clock; the application owns all animation phases.</summary>
public static partial class BrowserProbe
{
    internal static TransitionScene? Scene { get; set; }

    /// <summary>Gets startup readiness after logical initial navigation.</summary>
    /// <returns>Whether the primary window and navigator are available.</returns>
    [JSExport]
    public static bool IsPresented() => Scene is { Application.State: UiApplicationState.Running, Context: not null, Navigation: not null };

    /// <summary>Gets the host failure for test diagnostics.</summary>
    /// <returns>The host state and failure.</returns>
    [JSExport]
    public static string GetState() => $"{Scene?.Application?.State};{Scene?.Navigation?.CaptureSnapshot().Phase};{Scene?.Application?.Failure}";

    /// <summary>Gets the committed route.</summary>
    /// <returns>The current route.</returns>
    [JSExport]
    public static string GetRoute() => Scene?.Context?.View.CaptureNavigationSnapshot().Current?.Route ?? string.Empty;

    /// <summary>Gets the delivered entry notification count.</summary>
    /// <returns>The count.</returns>
    [JSExport]
    public static int GetEntered() => Scene?.Entered ?? 0;

    /// <summary>Gets the disposed scoped service count.</summary>
    /// <returns>The count.</returns>
    [JSExport]
    public static int GetDisposed() => Scene?.Disposed ?? 0;

    /// <summary>Gets the visual result without changing the animation.</summary>
    /// <returns>The pending state or closed result.</returns>
    [JSExport]
    public static string GetVisual() => Scene?.LastRequest?.VisualCompletion is { IsCompletedSuccessfully: true } task ? $"{task.Result.Status};{task.Result.Code}" : "pending";

    /// <summary>Gets delivered exit notifications.</summary>
    /// <returns>The count.</returns>
    [JSExport]
    public static int GetExited() => Scene?.Exited ?? 0;

    /// <summary>Requests controlled element exit through its model.</summary>
    /// <returns>Queued owner work.</returns>
    [JSExport]
    public static async Task HideAsync() => await Scene!.Application!.Dispatcher.InvokeAsync(() => Scene.ShowGroup.Value = false);

    /// <summary>Updates live group content without restarting its current fade.</summary>
    /// <param name="green">Whether the top child is green.</param>
    /// <returns>Queued owner work.</returns>
    [JSExport]
    public static async Task SetGroupGreenAsync(bool green) => await Scene!.Application!.Dispatcher.InvokeAsync(() => Scene.GroupColor.Value = green ? new(0, 1, 0, 1) : new(0, 0, 1, 1));

    /// <summary>Updates the live target page while its input remains gated.</summary>
    /// <returns>Queued owner work.</returns>
    [JSExport]
    public static async Task SetPageGreenAsync() => await Scene!.Application!.Dispatcher.InvokeAsync(() => Scene.PageColor.Value = new(0, 1, 0, 1));

    /// <summary>Requests ordinary back navigation with connected effects.</summary>
    /// <returns>Queued owner work.</returns>
    [JSExport]
    public static async Task BackAsync() => await Scene!.Application!.Dispatcher.InvokeAsync(() => Scene.LastRequest = Scene.Navigation!.RequestBack());

    /// <summary>Requests forward navigation while skipping only its effects.</summary>
    /// <returns>Queued owner work.</returns>
    [JSExport]
    public static async Task ForwardAsync() => await Scene!.Application!.Dispatcher.InvokeAsync(() =>
    {
        Scene.LastRequest = Scene.Navigation!.RequestForward();
        Scene.LastRequest.SkipAnimation();
    });

    /// <summary>Reads the page gate through the normal semantic snapshot.</summary>
    /// <returns>Whether the current page's primary action is gated.</returns>
    [JSExport]
    public static async Task<bool> IsGatedAsync()
    {
        UiAutomationSnapshot snapshot = await new UiAutomationPage(Scene!.Context!.Automation).SnapshotAsync();
        return snapshot.Nodes.Any(node => node.IsTransitioning && node.TestId is "next" or "quit");
    }

    /// <summary>Reads an ordinary action's current semantic center.</summary>
    /// <param name="id">The test identity.</param>
    /// <returns>The logical center as a JSON pair.</returns>
    [JSExport]
    public static async Task<string> PointAsync(string id)
    {
        UiAutomationSnapshot snapshot = await new UiAutomationPage(Scene!.Context!.Automation).SnapshotAsync();
        UiAutomationNode node = snapshot.Nodes.Single(node => node.TestId == id);
        return FormattableString.Invariant($"[{node.Bounds.X + (node.Bounds.Width / 2)},{node.Bounds.Y + (node.Bounds.Height / 2)}]");
    }

    /// <summary>Requests a controlled Hosted popup.</summary>
    /// <param name="open">The model's visibility intention.</param>
    /// <returns>Queued owner work.</returns>
    [JSExport]
    public static async Task PopupAsync(bool open) => await Scene!.Application!.Dispatcher.InvokeAsync(() => Scene.PopupOpen.Value = open);

    /// <summary>Gets completed popup cleanup notifications.</summary>
    /// <returns>The count.</returns>
    [JSExport]
    public static int GetPopupClosed() => Scene?.PopupClosed ?? 0;

    /// <summary>Gets the normal manager's open popup count.</summary>
    /// <returns>The count.</returns>
    [JSExport]
    public static int GetPopupCount() => Scene?.Context?.Popups.CapturePopups().Count ?? 0;

    /// <summary>Gets the controlled popup's preparation state.</summary>
    /// <returns>The state or absent.</returns>
    [JSExport]
    public static string GetPopupState() => Scene?.Context?.Popups.CapturePopups().SingleOrDefault()?.State.ToString() ?? $"absent;{Scene?.PopupCloseCode}";

    /// <summary>Reads the popup location from the window manager.</summary>
    /// <returns>The logical center as a JSON pair.</returns>
    [JSExport]
    public static string PopupPoint()
    {
        Lumyte.UI.Windowing.UiWindowSnapshot state = Scene!.Context!.Windows.CaptureWindows().Single(window => window.Role == Lumyte.UI.Windowing.UiWindowRole.Popup);
        return FormattableString.Invariant($"[{state.BoundsInPresentation.X + 20},{state.BoundsInPresentation.Y + 20}]");
    }

    /// <summary>Moves only the borrowed test clock.</summary>
    /// <param name="seconds">The nonnegative elapsed interval.</param>
    /// <returns>Queued owner work.</returns>
    [JSExport]
    public static async Task AdvanceAsync(double seconds) => await Scene!.Application!.Dispatcher.InvokeAsync(() => Scene.Clock.Advance(Duration.FromSeconds(seconds)));

    /// <summary>Requests a normal compositor PNG while element and page gates remain closed.</summary>
    /// <returns>The base64 PNG.</returns>
    [JSExport]
    public static async Task<string> CapturePngAsync()
    {
        var page = new UiAutomationPage(Scene!.Context!.Automation, new() { Timeout = TimeSpan.FromSeconds(30) });
        return Convert.ToBase64String(await page.ScreenshotAsync());
    }
}
