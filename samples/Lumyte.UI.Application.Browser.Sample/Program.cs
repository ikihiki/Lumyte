using System.Runtime.Versioning;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Mathematics.Colors;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Browser;
using Lumyte.UI.Application.Browser.Sample;
using static Lumyte.UI.Compose;

[assembly: SupportedOSPlatform("browser")]

UiApplicationBuilder builder = UiApplication.CreateBuilder(args).Configure(new() { AnimationClock = BrowserProbe.Clock, DrawingRetention = DrawingCompose.RetentionGpuLimits(maxFramesInFlight: 2, maxCachedAssets: 512, maxCachedBytes: 64 * 1024 * 1024, maxWorkingBytes: 256 * 1024 * 1024) }).UseBrowser();
builder.UseRoot(context =>
{
    BrowserProbe.Context = context;
    BrowserProbe.Width = Animate(BrowserProbe.Target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear, () => BrowserProbe.Completed++);
    return Button(width: BrowserProbe.Width, height: 48, testId: "quit", onClick: context.RequestStop)[Rectangle(width: BrowserProbe.Width, height: 48, fill: new SRgba<float>(0.5f, 0.5f, 0.5f, 1))];
});
await using UiApplication app = builder.Build();
BrowserProbe.Application = app;
await app.RunAsync();
