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
    var files = new Dictionary<MapTileId, string>();
    for (int zoom = 0; zoom <= 2; zoom++)
    {
        int count = 1 << zoom;
        for (int x = 0; x < count; x++)
        {
            for (int y = 0; y < count; y++)
            {
                files.Add(new(zoom, x, y), file);
            }
        }
    }

    var registration = new LocalMvtRegistration("handmade", "v1", new MapTileLoadProfile("sample"), files);
    UiApplicationBuilder builder = UiApplication.CreateBuilder();
    if (desktop)
    {
        builder.UseDesktop(new() { Title = "Lumyte Maps — hand-authored fixture (no OSM data)", ClientSize = new(512, 256) });
    }
    else
    {
        builder.UseHeadless(new() { ClientSize = new(512, 256) });
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
        while (!await app.Dispatcher.InvokeAsync(() => content?.AdvanceSmokePath() == true, deadline.Token))
        {
            await Task.Delay(1, deadline.Token);
        }

        await app.StopAsync();
    }

    await run;
    Console.WriteLine(desktop ? "Map sample stopped cleanly." : "Map sample headless spatial path completed: actual tile arrivals, manager reclamation and orderly stop; record-only, no GPU pixel claim.");
}
finally
{
    Directory.Delete(directory, recursive: true);
}
