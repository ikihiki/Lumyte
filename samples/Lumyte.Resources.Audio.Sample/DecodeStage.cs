using Lumyte.Resources.Execution;

namespace Lumyte.Resources.Audio.Sample;

internal sealed class DecodeStage(ResourceJob<DecodeRequest, AudioPcmData, DecodeHandler> jobs) : IResourceSourceStage<AudioPcmData>
{
    public async ValueTask<AudioPcmData> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using Stream stream = await snapshot.OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes, cancellationToken).ConfigureAwait(false);
        return await jobs.Execute(new DecodeRequest(bytes.ToArray()), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask ReleaseAsync(AudioPcmData resource) => ValueTask.CompletedTask;
}
