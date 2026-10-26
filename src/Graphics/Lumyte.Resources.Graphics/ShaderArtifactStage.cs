using Lumyte.Graphics.Abstractions;
using Lumyte.Memory;

namespace Lumyte.Resources.Graphics;

/// <summary>Loads precompiled shader binaries through tracked source inputs.</summary>
public sealed class ShaderArtifactStage : IResourceSourceStage<ShaderArtifact>
{
    /// <inheritdoc/>
    public async ValueTask<ShaderArtifact> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
        await using Stream input = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);
        ShaderArtifact artifact;
        if (snapshot.Length is { } length)
        {
            int count = checked((int)length);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
            _ = await context.GetMemoryScope(ResourceDependencyLifetime.Resident).ReserveAsync(
            new()
            {
                Pool = new("cpu"),
                Bytes = checked((ulong)count),
                Purpose = MemoryPurpose.Resident,
            },
            cancellationToken);
            using IMemoryBuffer<byte> bytes = await context.GetMemoryScope(ResourceDependencyLifetime.Build)
                .RentAsync<byte>(new("cpu"), count, MemoryPurpose.Build, cancellationToken);
            await input.ReadExactlyAsync(bytes.Memory[..count], cancellationToken);
            return new(bytes.Memory.Span[..count]);
        }

        artifact = await ShaderArtifact.LoadAsync(input, cancellationToken);
        _ = await context.GetMemoryScope(ResourceDependencyLifetime.Resident).ReserveAsync(
            new()
            {
                Pool = new("cpu"),
                Bytes = checked((ulong)artifact.BinaryLength),
                Purpose = MemoryPurpose.Resident,
            },
            cancellationToken);
        return artifact;
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(ShaderArtifact resource) => ValueTask.CompletedTask;
}
