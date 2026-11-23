using Lumyte.Composition;
using Lumyte.Mathematics.Colors;
using Lumyte.Signals;
using static Lumyte.UI.Compose;
using static Lumyte.UI.WebView.UiWebViewHooks;
using WebCompose = Lumyte.UI.WebView.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.WebView.Cef.Sample;

/// <summary>Mixes native UI and an optional browser without passing a window or session.</summary>
public static partial class BrowserSampleUi
{
    /// <summary>Contains retained function definitions; call the generated factories.</summary>
    public static partial class Definitions
    {
        /// <summary>Builds ordinary native controls around a nested browser component.</summary>
        /// <param name="initialUri">The explicit initial document.</param>
        /// <returns>A fresh mixed UI declaration.</returns>
        [Composable(Factory = "BrowserSampleUi")]
        public static Widget MixedPage(Uri initialUri)
        {
            Signal<int> clicks = UseSignal(0);
            int count = clicks.Value;
            Widget header = Rectangle(width: 688, height: 24, fill: new SRgba<float>(0.1f, 0.25f, 0.5f, 1), automationName: "Native header");
            Widget counterBar = Rectangle(width: 32 + ((count % 8) * 12), height: 24, fill: new SRgba<float>(0.1f, 0.7f, 0.25f, 1));
            Widget counter = Button(width: 160, height: 40, testId: "native-counter", automationName: $"Native counter: {count}", onClick: () => clicks.Value++)[counterBar];
            Widget browserPane = BrowserSampleUi.BrowserPane(initialUri, key: "browser-pane");
            return Stack(spacing: 8, with: [Padding.All(12)])[header, counter, browserPane];
        }

        /// <summary>Gets its borrowed browser from the automatically provided window context.</summary>
        /// <param name="initialUri">The immutable address for this session key.</param>
        /// <returns>A clipped inset browser among ordinary UI containers.</returns>
        [Composable(Factory = "BrowserSampleUi")]
        public static Widget BrowserPane(Uri initialUri)
        {
            UiWebViewSession browser = UseWebView("page", initialUri);
            Widget view = WebCompose.WebView(session: browser, width: 640, height: 360, automationName: "Optional browser");
            Widget script = Button(width: 160, height: 32, automationName: "Evaluate document title", onClick: async () =>
            {
                try
                {
                    string json = await browser.EvaluateScriptAsync("document.title");
                    Console.Error.WriteLine($"Browser title JSON: {json}");
                }
                catch (Exception error) when (error is InvalidOperationException or NotSupportedException or UiWebViewScriptException or OperationCanceledException)
                {
                    Console.Error.WriteLine($"Browser script unavailable: {error.GetType().Name}");
                }
            });
            return Stack(spacing: 8)[script, Panel(width: 688, height: 400, clipToBounds: true, with: [Padding.All(12)], automationName: "Browser container")[view]];
        }
    }
}
