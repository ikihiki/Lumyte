using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Toolchains.CsProj;

namespace Lumyte.Graphics.Benchmarks;

internal sealed class ExplicitProjectGenerator() : CsProjGenerator("net10.0", null!, null!, null!)
{
    protected override FileInfo GetProjectFilePath(Type benchmarkTarget, ILogger logger)
        => new(Path.Combine(AppContext.BaseDirectory, "../../../Lumyte.Graphics.Benchmarks.csproj"));
}
