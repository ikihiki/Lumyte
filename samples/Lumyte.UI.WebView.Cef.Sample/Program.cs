using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using Lumyte.UI.Application.WebView.Cef;
using static Lumyte.UI.WebView.Compose;

if (CefWebViewBootstrap.TryRunSubprocess(args, out int exitCode))
{
    return exitCode;
}

if (!args.Contains("--enable-experimental-cef", StringComparer.Ordinal))
{
    Console.Error.WriteLine("CEF 120 is experimental and not production-recommended. Explicit --enable-experimental-cef is required. Sandbox is never disabled.");
    return 2;
}

Uri uri = new(args.FirstOrDefault(value => value.StartsWith("http://", StringComparison.Ordinal) || value.StartsWith("https://", StringComparison.Ordinal)) ?? "https://example.com/");
UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseDesktop();
builder.UseCefWebViews(new());
builder.UseRoot(window => WebView(session: window.GetWebViewSession("page", uri), width: 800, height: 600, automationName: "Experimental browser"));
await using UiApplication app = builder.Build();
await app.RunAsync();
return 0;
