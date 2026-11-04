using System.Text.Json;
using Lumyte.Graphics.Effects.Benchmarks;

try
{
    await EffectRunner.RunAsync(args);
}
catch (Exception error)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "failed", error = error.ToString() }));
    Environment.ExitCode = 1;
}
