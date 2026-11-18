using Lumyte.Composition;
using Lumyte.Graphics.Text;
using Lumyte.Signals;
using Lumyte.UI.Media;
using static Lumyte.UI.Compose;
using static Lumyte.UI.WebView.UiWebViewHooks;
using WebCompose = Lumyte.UI.WebView.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Real generated function components nested in ordinary native UI; no window or session arguments.</summary>
public static partial class WebViewFunctionUi
{
    /// <summary>Contains generated function definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>Builds native UI around a nested browser component.</summary>
        /// <param name="probe">Ordinary observation inputs.</param>
        /// <returns>The native layout.</returns>
        [Composable(Factory = "WebViewFunctionUi")]
        public static Widget Root(Probe probe) => Panel(width: 220, height: 170, clipToBounds: true)[
            Stack(spacing: 7)[
                Lumyte.UI.Media.Compose.Text(value: "Native help", layoutProvider: Use(() => new LayoutProvider())),
                Button(width: 90, height: 24, automationName: "native-before", onClick: () => probe.Clicks++),
                WebViewFunctionUi.HelpPane(probe),
                Button(width: 90, height: 24, automationName: "native-after", onClick: () => probe.Clicks++)]];

        /// <summary>Calls the hook only while the generated retained function is evaluated.</summary>
        /// <param name="probe">Ordinary observation inputs.</param>
        /// <returns>A nested browser and native control.</returns>
        [Composable(Factory = "WebViewFunctionUi")]
        public static Widget HelpPane(Probe probe)
        {
            int revision = probe.Revision.Value;
            Signal<int> local = UseSignal(0);
            _ = local.Value;
            UiWebViewSession browser = UseWebView(probe.SessionKey, probe.InitialUri);
            probe.Sessions.Add(browser);
            probe.Tokens.Add(Use(() => new object()));
            probe.Click = () =>
            {
                _ = browser.State;
                local.Value++;
                probe.Clicks++;
            };
            Action<UiWebMessageReceivedEventArgs> receive = message =>
            {
                local.Value++;
                probe.Messages++;
                probe.LastMessageRevision = revision;
                probe.LastMessage = message.Json;
            };
            return Stack(spacing: 3)[
                Button(width: 120, height: 18, automationName: "browser-state", onClick: probe.Click),
                probe.ShowView ? WebCompose.WebView(session: browser, width: 180, height: 110, automationName: "help-browser", onWebMessageReceived: receive) : Panel(width: 180, height: 110)];
        }
    }

    /// <summary>Test observations and ordinary component inputs, not a browser service.</summary>
    public sealed class Probe
    {
        /// <summary>Gets or sets the requested identity.</summary>
        public string SessionKey { get; set; } = "help";

        /// <summary>Gets or sets the initial address.</summary>
        public Uri InitialUri { get; set; } = new("https://example.invalid/help");

        /// <summary>Gets the independently observed signal.</summary>
        public Signal<int> Revision { get; } = Signal.Create(0);

        /// <summary>Gets or sets a value indicating whether to mount the browser view.</summary>
        public bool ShowView { get; set; } = true;

        /// <summary>Gets the observations.</summary>
        public List<UiWebViewSession> Sessions { get; } = [];

        /// <summary>Gets retained function tokens.</summary>
        public List<object> Tokens { get; } = [];

        /// <summary>Gets or sets the captured ordinary button callback.</summary>
        public Action? Click { get; set; }

        /// <summary>Gets the number of declarative message callbacks.</summary>
        public int Messages { get; internal set; }

        /// <summary>Gets the callback's captured render revision.</summary>
        public int LastMessageRevision { get; internal set; }

        /// <summary>Gets the latest payload.</summary>
        public string? LastMessage { get; internal set; }

        /// <summary>Gets the number of button invocations.</summary>
        public int Clicks { get; internal set; }
    }

    private sealed class LayoutProvider : ITextLayoutProvider
    {
        public TextLayout2D CreateLayout(string value, float maximumWidth) =>
            TextLayout2D.FromDrawings(value, default, new(0, 0, Math.Min(90, maximumWidth), 12), 1, []);
    }
}
