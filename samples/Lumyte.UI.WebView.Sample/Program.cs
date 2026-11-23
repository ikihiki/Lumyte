using Lumyte.UI;
using Lumyte.UI.WebView;
using WebViewCompose = Lumyte.UI.WebView.Compose;

if (args.Any(argument => argument != "--selfcheck"))
{
    Console.Error.WriteLine("Usage: Lumyte.UI.WebView.Sample [--selfcheck] (engine-free; no browser, GPU or network)");
    return 1;
}

var commands = new List<WebViewCommand>();
var session = new UiWebViewSession(new("https://example.invalid/help"), () => { }, commands.Add);
await session.NavigateAsync(new("https://example.invalid/next"));
Check(commands.Count == 0, "Creating must defer commands.");
session.Update(UiWebViewState.Ready);
Check(commands.Count == 1 && commands[0].Sequence == 1, "Ready must accept the queued navigation once.");
using (var surface = new UiSurface(WebViewCompose.WebView(session: session, automationName: "Help")))
{
    surface.SetViewport(new(320, 200));
    surface.UpdateLayout();
    UiElement element = surface.RootElement;
    surface.Rebuild(WebViewCompose.WebView(session: session, automationName: "Help rebuilt"));
    surface.UpdateLayout();
    Check(ReferenceEquals(element, surface.RootElement) && commands.Count == 1, "Rebuild must borrow, not navigate.");
}

Check(session.State == UiWebViewState.Ready && !session.ImeAvailable, "Unmount must not close the window-owned session or claim IME.");
var budget = new WebViewPixelBudget(8);
using (var mailbox = new WebViewPixelMailbox(budget))
{
    byte[] callback = [1, 2, 3, 255];
    Check(mailbox.Publish(callback, 1, 1, 0), "First callback must fit.");
    Array.Clear(callback);
    using WebViewPixelMailbox.PixelLease reader = mailbox.Take()!;
    Check(reader.Bytes[0] == 1, "Callback memory must be copied.");
    Check(mailbox.Publish(callback, 1, 1, 0) && budget.Used == 8, "Reader and latest must share the budget.");
    mailbox.Resize(1);
    Check(!mailbox.Publish(callback, 1, 1, 0) && budget.Used == 4, "Resize must reject stale callbacks without retiring readers.");
    mailbox.Dispose();
    Check(reader.Bytes[0] == 1 && budget.Used == 4, "Close must retain leased CPU pixels.");
}

Check(budget.Used == 0, "All CPU pixel leases must retire.");
session.Update(UiWebViewState.Closing);
session.Update(UiWebViewState.Closed);
Console.WriteLine("WebView selfcheck passed: engine-free queue, passive rebuild, borrowed lifetime, callback copy, bounded pixels, resize generation and lease retirement. No browser rendering/input/GPU/IME claim.");
return 0;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
