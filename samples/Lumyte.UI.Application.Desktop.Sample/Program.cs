using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using static Lumyte.UI.Compose;

UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseDesktop();
builder.UseRoot(context => Button(width: 160, height: 48, testId: "quit", onClick: context.RequestStop)[Rectangle(width: 160, height: 48, fill: new Color2D(0.5f, 0.5f, 0.5f, 1))]);
await using UiApplication app = builder.Build();
await app.RunAsync();
