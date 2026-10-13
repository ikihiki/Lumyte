using Lumyte.Graphics.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class ReadbackStage([FromKeyedServices("main")] IGraphicsDeviceService service) : IResourceSourceStage<CpuValue>
{
    public async ValueTask<CpuValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        await context.UseGraphicsAsync(service, "graphics.main", ResourceDependencyLifetime.Build, cancellationToken);
        return new();
    }

    public ValueTask ReleaseAsync(CpuValue resource) => ValueTask.CompletedTask;
}
