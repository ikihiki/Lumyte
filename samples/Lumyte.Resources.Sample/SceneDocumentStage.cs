using System.Text.Json;
using Lumyte.Memory;

namespace Lumyte.Resources.Sample;

internal sealed class SceneDocumentStage(SampleSettings settings, SampleCounters counters) : IResourceSourceStage<SceneDocument>
{
    public async ValueTask<SceneDocument> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
        int length = checked((int)(snapshot.Length ?? throw new NotSupportedException("This sample expects a known-length file.")));
        IMemoryScope memory = context.GetMemoryScope(ResourceDependencyLifetime.Resident);
        IMemoryBuffer<byte> encoded = await memory.RentAsync<byte>(settings.DocumentPool, length, MemoryPurpose.Resident, cancellationToken);
        await using Stream input = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);
        await input.ReadExactlyAsync(encoded.Memory, cancellationToken);

        // JsonDocument borrows the encoded memory. The manager ends its resident scope after ReleaseAsync.
        var document = JsonDocument.Parse(encoded.Memory);
        counters.RecordDocumentBuild();
        return new(document);
    }

    public ValueTask ReleaseAsync(SceneDocument resource)
    {
        resource.Dispose();
        return ValueTask.CompletedTask;
    }
}
