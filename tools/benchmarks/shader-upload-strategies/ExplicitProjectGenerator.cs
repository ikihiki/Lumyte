using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Toolchains.CsProj;

namespace Lumyte.Graphics.UploadStrategies.Benchmarks;

internal sealed class ExplicitProjectGenerator() : CsProjGenerator("net10.0", null!, null!, null!)
{
    protected override FileInfo GetProjectFilePath(Type benchmarkTarget, ILogger logger)
        => new(Path.Combine(AppContext.BaseDirectory, "../../../ShaderUploadStrategies.Benchmarks.csproj"));
}
