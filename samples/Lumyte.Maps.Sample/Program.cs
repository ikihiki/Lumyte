using Lumyte.Maps;
using Lumyte.Maps.Mvt;
using Lumyte.Maps.Sample;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources;
using Lumyte.Resources.DependencyInjection;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using Lumyte.UI.Application.Headless;
using Microsoft.Extensions.DependencyInjection;

bool desktop = args.Contains("desktop", StringComparer.Ordinal);
if (args.Any(argument => argument is not "headless" and not "desktop"))
{
    throw new ArgumentException("Use headless (bounded record-only smoke) or desktop (interactive).");
}

string directory = Path.Combine(Path.GetTempPath(), $"lumyte-maps-{Guid.NewGuid():N}");
Directory.CreateDirectory(directory);
try
{
    string file = Path.Combine(directory, "fixture.mvt");
    await File.WriteAllBytesAsync(file, LocalFixture.CreateTile());
    var registration = new LocalMvtRegistration("handmade", "v1", new MapTileLoadProfile("sample"), new Dictionary<MapTileId, string> { [new(0, 0, 0)] = file });
    UiApplicationBuilder builder = UiApplication.CreateBuilder();
    if (desktop)
    {
        builder.UseDesktop(new() { Title = "Lumyte Maps — hand-authored fixture (no OSM data)" });
    }
    else
    {
        builder.UseHeadless();
    }

    builder.Services.AddMemoryManagement();
    builder.Services.AddSingleton(registration);
    builder.Services.AddResources(resources => resources.UseSource<LocalMvtSource>().AddSourcePipeline<MapTileContent, MvtResourceStage>());
    MapSampleContent? content = null;
    builder.UseContent(services => content = new MapSampleContent(services.GetRequiredService<ResourceManager>(), registration));
    await using UiApplication app = builder.Build();
    Task run = app.RunAsync();
    if (!desktop)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await app.Dispatcher.InvokeAsync(() => content?.HasLoadedTile == true, deadline.Token))
        {
            await Task.Delay(1, deadline.Token);
        }

        await app.StopAsync();
    }

    await run;
    Console.WriteLine(desktop ? "Map sample stopped cleanly." : "Map sample headless record-only host completed; no GPU pixel claim.");
}
finally
{
    Directory.Delete(directory, recursive: true);
}
