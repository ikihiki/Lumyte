using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Direct3D12;
using Lumyte.Graphics.Direct3D12.Sample;
using Lumyte.Graphics.Samples;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Direct3D 12 requires Windows.");
    return 1;
}

string backend;
string exercise;
bool partial = args.Contains("partial");
string[] selections = args.Where(value => value != "partial").ToArray();
try
{
    if (args.Count(value => value == "partial") > 1 || selections.Length > 2)
    {
        throw new ArgumentException("Usage: hardware|warp [caps|suite|window|exercise] [partial]");
    }

    backend = Lumyte.Samples.Framework.SampleSelector.SelectValue(selections.ElementAtOrDefault(0), ["hardware", "warp"], "backend");
    exercise = Lumyte.Samples.Framework.SampleSelector.SelectValue(selections.ElementAtOrDefault(1), ["caps", .. GraphicsExercises.Names, "suite", "window"], "exercise");
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

if (exercise == "window")
{
    return WindowSample.Run(backend == "warp", partial);
}

using var owner = Direct3D12Device.Create(new() { UseWarp = backend == "warp", EnablePartialGraphicsPrograms = partial });
IGraphicDevice device = owner;
await GraphicsExercises.RunAsync(device, exercise, Console.WriteLine);
Console.WriteLine($"PASS: Agility SDK {owner.SdkVersion}, partial programs: {owner.PartialGraphicsProgramsEnabled}.");
return 0;
