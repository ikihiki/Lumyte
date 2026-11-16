using Lumyte.Resources;

namespace Lumyte.Maps.Mvt;

/// <summary>Reusable Resources stage opening the observed local source through the build context.</summary>
/// <param name="registration">The same registration used by the source.</param>
public sealed class MvtResourceStage(LocalMvtRegistration registration) : IResourceSourceStage<MapTileContent>
{
    /// <inheritdoc />
    public async ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        registration.Validate(context.Address);
        ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
        if (snapshot is not LocalMvtSource.Snapshot local || !ReferenceEquals(local.Registration, registration))
        {
            throw new InvalidOperationException("The registered local source must supply an owned bounded memory snapshot.");
        }

        return MvtDecoder.Decode(local.Content, registration.Profile, snapshot.Revision);
    }

    /// <inheritdoc />
    public ValueTask ReleaseAsync(MapTileContent resource) => ValueTask.CompletedTask;
}
