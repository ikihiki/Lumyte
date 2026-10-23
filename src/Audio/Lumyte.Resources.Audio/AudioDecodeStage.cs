using Lumyte.Resources.Execution;

namespace Lumyte.Resources.Audio;

/// <summary>Reads an observed source asynchronously and decodes WAVE on the configured CPU executor.</summary>
/// <param name="jobs">The decode job collected by the execution runtime.</param>
public sealed class AudioDecodeStage(ResourceJob<AudioDecodeRequest, AudioPcmData, AudioDecodeHandler> jobs) : IResourceSourceStage<AudioPcmData>
{
    /// <inheritdoc/>
    public async ValueTask<AudioPcmData> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using Stream stream = await snapshot.OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes, cancellationToken).ConfigureAwait(false);
        return await jobs.Execute(new(bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length))), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(AudioPcmData resource) => ValueTask.CompletedTask;
}
