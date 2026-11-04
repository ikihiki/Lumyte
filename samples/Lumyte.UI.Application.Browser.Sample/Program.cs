using System.Runtime.Versioning;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Browser;
using Lumyte.UI.Application.Browser.Sample;
using static Lumyte.UI.Compose;

[assembly: SupportedOSPlatform("browser")]

UiApplicationBuilder builder = UiApplication.CreateBuilder(args).Configure(new() { AnimationClock = BrowserProbe.Clock }).UseBrowser();
builder.UseRoot(context =>
{
    BrowserProbe.Context = context;
    BrowserProbe.Width = Animate(BrowserProbe.Target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear, () => BrowserProbe.Completed++);
    return Button(width: BrowserProbe.Width, height: 48, testId: "quit", onClick: context.RequestStop)[Rectangle(width: BrowserProbe.Width, height: 48, fill: new Color2D(0.5f, 0.5f, 0.5f, 1))];
});
await using UiApplication app = builder.Build();
BrowserProbe.Application = app;
await app.RunAsync();
