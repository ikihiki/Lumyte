using System.Runtime.Versioning;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Browser;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Navigation;
using Lumyte.UI.Navigation.Browser.Sample;
using Lumyte.UI.Navigation.SampleSupport;

[assembly: SupportedOSPlatform("browser")]

UiApplication? application = null;
var pages = new ExamplePages(() => application!.RequestStop());
UiNavigationApplicationContent? content = null;
content = new(pages, navigation => navigation.RequestNavigate(ExamplePages.Home, default), onCompleted: result =>
{
    if (result.Status == UiNavigationStatus.Committed)
    {
        BrowserProbe.Context = content!.WindowContext;
    }
});
UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseBrowser().UseContent(_ => content);
await using UiApplication app = application = builder.Build();
BrowserProbe.Application = app;
await app.RunAsync();
