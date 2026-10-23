using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;

namespace Lumyte.Resources.Audio.Sample;

internal sealed class TestFactory : IAudioDeviceFactory
{
    public Task<IAudioDevice> CreateAsync() => Task.FromResult<IAudioDevice>(new TestDevice());
}
