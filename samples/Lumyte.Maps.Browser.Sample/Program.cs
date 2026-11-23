using System.Runtime.Versioning;
using Lumyte.Maps;
using Lumyte.Maps.Browser.Sample;
using Lumyte.Maps.Mvt;
using Lumyte.Maps.Sample;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources;
using Lumyte.Resources.DependencyInjection;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Browser;
using Microsoft.Extensions.DependencyInjection;

[assembly: SupportedOSPlatform("browser")]

byte[] fixture = LocalFixture.CreateTile();
var registration = new InMemoryMvtRegistration("handmade", "v1", new MapTileLoadProfile("browser-sample"), new Dictionary<MapTileId, ReadOnlyMemory<byte>> { [new(0, 0, 0)] = fixture, [new(1, 0, 0)] = fixture, [new(1, 0, 1)] = fixture, [new(1, 1, 0)] = fixture, [new(1, 1, 1)] = fixture, [new(2, 1, 1)] = fixture });
UiApplicationBuilder builder = UiApplication.CreateBuilder(args).UseBrowser(new UiBrowserApplicationOptions { OwnWheelDefaults = true });
builder.Services.AddMemoryManagement();
builder.Services.AddSingleton(registration);
builder.Services.AddResources(resources => resources.UseSource<InMemoryMvtSource>().AddSourcePipeline<MapTileContent, MvtResourceStage>());
builder.UseContent(services => BrowserProbe.Content = new BrowserMapContent(services.GetRequiredService<ResourceManager>(), registration));
await using UiApplication app = builder.Build();
BrowserProbe.Application = app;
await app.RunAsync();
Console.WriteLine("Maps Browser stopped cleanly after bounded in-memory MVT adoption.");
