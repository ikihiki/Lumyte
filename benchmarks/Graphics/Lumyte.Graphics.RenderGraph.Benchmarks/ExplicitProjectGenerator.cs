using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Toolchains.CsProj;

namespace Lumyte.Graphics.RenderGraph.Benchmarks;

internal sealed class ExplicitProjectGenerator() : CsProjGenerator("net10.0", null!, null!, null!)
{
    protected override FileInfo GetProjectFilePath(Type benchmarkTarget, ILogger logger)
        => new(Path.Combine(AppContext.BaseDirectory, "../../../Lumyte.Graphics.RenderGraph.Benchmarks.csproj"));
}
