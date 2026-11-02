using System.Runtime.Versioning;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Browser;
using Lumyte.UI.Navigation.Browser.Sample;
using Lumyte.UI.Pages.SampleSupport;

[assembly: SupportedOSPlatform("browser")]

UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseBrowser();
SampleState state = ExamplePages.Configure(builder, context => BrowserProbe.Context = context);
await using UiApplication app = builder.Build();
state.Stop = app.RequestStop;
BrowserProbe.Application = app;
await app.RunAsync();
if (state.Created != 2 || state.Disposed != 2)
{
    throw new InvalidOperationException("The DI page scopes were not created and disposed exactly once.");
}
