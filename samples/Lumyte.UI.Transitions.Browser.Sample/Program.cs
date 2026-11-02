using System.Runtime.Versioning;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Browser;
using Lumyte.UI.Transitions.Browser.Sample;
using Lumyte.UI.Transitions.SampleSupport;
using Lumyte.UI.Windowing;

[assembly: SupportedOSPlatform("browser")]

var scene = new TransitionScene();
UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseBrowser();
scene.Configure(builder, args.Contains("--sequential", StringComparer.Ordinal), args.Contains("--same-content", StringComparer.Ordinal));
builder.UseWindowManager(_ => new HostedUiWindowManager(new PresentationTestClock(), "browser-hosted-only"));
await using UiApplication app = builder.Build();
scene.Application = app;
BrowserProbe.Scene = scene;
await app.RunAsync();
