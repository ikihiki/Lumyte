using System.Runtime.Versioning;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Browser;
using Lumyte.UI.Application.Browser.Sample;
using static Lumyte.UI.Compose;

[assembly: SupportedOSPlatform("browser")]

UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseBrowser();
builder.UseRoot(context =>
{
    BrowserProbe.Context = context;
    return Button(width: 160, height: 48, testId: "quit", onClick: context.RequestStop)[Rectangle(width: 160, height: 48, fill: new Color2D(0.5f, 0.5f, 0.5f, 1))];
});
await using UiApplication app = builder.Build();
BrowserProbe.Application = app;
await app.RunAsync();
