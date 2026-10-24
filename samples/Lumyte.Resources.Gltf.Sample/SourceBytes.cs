using Lumyte.Memory;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed record SourceBytes(IMemoryBuffer<byte> Buffer)
{
    internal static async ValueTask<SourceBytes> ReadAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
        int length = checked((int)(snapshot.Length ?? throw new NotSupportedException("A known-length source is required.")));
        if (length is <= 0 or > 64 * 1024 * 1024)
        {
            throw new NotSupportedException("Sample inputs must be between 1 byte and 64 MiB.");
        }

        IMemoryBuffer<byte> buffer = await context.GetMemoryScope(ResourceDependencyLifetime.Resident).RentAsync<byte>(new("gltf.cpu"), length, MemoryPurpose.Resident, cancellationToken);
        await using Stream stream = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);
        await stream.ReadExactlyAsync(buffer.Memory, cancellationToken);
        return new(buffer);
    }
}
