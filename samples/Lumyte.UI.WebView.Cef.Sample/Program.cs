using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using Lumyte.UI.Application.WebView.Cef;
using Lumyte.UI.WebView;
using static Lumyte.UI.Compose;
using static Lumyte.UI.WebView.Compose;

if (args.Contains("--selfcheck", StringComparer.Ordinal))
{
    UiApplicationBuilder check = UiApplication.CreateBuilder(args).UseDesktop();
    check.UseCefWebViews(new());
    check.UseRoot(_ => Button(width: 80, height: 40));
    await using UiApplication configured = check.Build();
    Console.WriteLine("Optional browser configuration selfcheck passed; no display, CEF, helper or GPU initialized.");
    return 0;
}

UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseDesktop();
bool enabled = args.Contains("--enable-experimental-cef", StringComparer.Ordinal);
if (enabled)
{
    builder.UseCefWebViews(new() { HelperExecutablePath = Environment.GetEnvironmentVariable("LUMYTE_CEF_HELPER") });
}

builder.UseRoot(window =>
{
    if (!enabled)
    {
        return Button(width: 800, height: 80, automationName: "Native UI — optional browser disabled");
    }

    UiWebViewSession page = window.GetWebViewSession("page", new("https://example.com/"));
    page.StateChanged += (_, _) =>
    {
        if (page.State == UiWebViewState.Faulted && window.View.CurrentSurface is { } surface)
        {
            surface.Rebuild(Button(width: 800, height: 80, automationName: "Native UI — " + page.FailureCode));
        }
    };

    // Native controls remain the application. The read-only browser is an optional fallback.
    return page.State == UiWebViewState.Faulted
        ? Button(width: 800, height: 80, automationName: "Native UI — " + page.FailureCode)
        : WebView(session: page, width: 800, height: 600, automationName: "Optional read-only browser");
});
await using UiApplication app = builder.Build();
await app.RunAsync();
return 0;
