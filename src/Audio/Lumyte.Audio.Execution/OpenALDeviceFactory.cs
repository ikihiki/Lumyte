using Lumyte.Audio.Abstractions;
using Lumyte.Audio.OpenAL;

namespace Lumyte.Audio.Execution;

/// <summary>Creates OpenAL on the execution owner.</summary>
public sealed class OpenALDeviceFactory : IAudioDeviceFactory
{
    /// <inheritdoc/>
    public Task<IAudioDevice> CreateAsync() => Task.FromResult<IAudioDevice>(new OpenALDevice());
}
