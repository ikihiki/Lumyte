using Lumyte.Resources;

namespace Lumyte.Maps.Mvt;

/// <summary>Reusable Resources stage accepting only the bounded snapshot owned by its exact registration.</summary>
public sealed class MvtResourceStage : IResourceSourceStage<MapTileContent>
{
    private readonly BoundedMvtRegistration? _bounded;
    private readonly LocalMvtRegistration? _local;
    private readonly InMemoryMvtRegistration? _memory;

    /// <summary>Initializes a new instance of the <see cref="MvtResourceStage"/> class for local files.</summary>
    /// <param name="registration">Registration shared with the source.</param>
    public MvtResourceStage(LocalMvtRegistration registration)
    {
        _local = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    /// <summary>Initializes a new instance of the <see cref="MvtResourceStage"/> class for portable memory input.</summary>
    /// <param name="registration">Registration shared with the source.</param>
    public MvtResourceStage(InMemoryMvtRegistration registration)
    {
        _memory = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    /// <summary>Initializes a new instance of the <see cref="MvtResourceStage"/> class for bounded transport input.</summary>
    /// <param name="registration">Exact registration shared with the source.</param>
    public MvtResourceStage(BoundedMvtRegistration registration)
    {
        _bounded = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    /// <inheritdoc />
    public async ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        if (_bounded is not null)
        {
            _bounded.Validate(context.Address);
        }
        else if (_local is not null)
        {
            _local.Validate(context.Address);
        }
        else
        {
            _memory!.Validate(context.Address);
        }

        ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
        if (snapshot is LocalMvtSource.Snapshot local && ReferenceEquals(local.Registration, _local))
        {
            return MvtDecoder.Decode(local.Content, _local!.Profile, snapshot.Revision);
        }

        if (snapshot is InMemoryMvtSource.Snapshot memory && ReferenceEquals(memory.Registration, _memory))
        {
            return MvtDecoder.Decode(memory.Content, _memory!.Profile, snapshot.Revision);
        }

        if (snapshot is BoundedMvtRegistration.Snapshot bounded && ReferenceEquals(bounded.Registration, _bounded))
        {
            return MvtDecoder.Decode(bounded.Content, _bounded!.Profile, snapshot.Revision);
        }

        throw new InvalidOperationException("The registered source must supply its own bounded memory snapshot.");
    }

    /// <inheritdoc />
    public ValueTask ReleaseAsync(MapTileContent resource) => ValueTask.CompletedTask;
}
