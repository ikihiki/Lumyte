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

bool allowUnsandboxed = args.Contains("--allow-unsandboxed-cef", StringComparer.Ordinal);
string? uriArgument = args.FirstOrDefault(value => value.StartsWith("--browser-uri=", StringComparison.Ordinal));
if (allowUnsandboxed && (uriArgument is null || !args.Contains("--enable-experimental-cef", StringComparer.Ordinal)))
{
    Console.Error.WriteLine("Unsafe CEF requires --enable-experimental-cef and explicit --browser-uri=http(s)://... (trusted local content only).");
    return 2;
}

if (!Uri.TryCreate(uriArgument is null ? "https://example.com/" : uriArgument[14..], UriKind.Absolute, out Uri? browserUri)
    || browserUri.Scheme is not ("http" or "https"))
{
    Console.Error.WriteLine("Expected absolute HTTP/S --browser-uri.");
    return 2;
}

if (allowUnsandboxed)
{
    Console.Error.WriteLine("UNSAFE MODE: CEF sandbox disabled by explicit user choice; trusted local pages only. URL validation is not network isolation.");
}

UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseDesktop();
bool enabled = args.Contains("--enable-experimental-cef", StringComparer.Ordinal);
if (enabled)
{
    builder.UseCefWebViews(new() { HelperExecutablePath = Environment.GetEnvironmentVariable("LUMYTE_CEF_HELPER"), AllowUnsandboxed = allowUnsandboxed });
}

builder.UseRoot(window =>
{
    if (!enabled)
    {
        return Button(width: 800, height: 80, automationName: "Native UI — optional browser disabled");
    }

    UiWebViewSession page = window.GetWebViewSession("page", browserUri);
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
