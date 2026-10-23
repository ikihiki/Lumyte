using System.Runtime.Versioning;
using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Browser;

namespace Lumyte.Audio.Execution.Browser.Sample;

[SupportedOSPlatform("browser")]
internal sealed class BrowserDeviceFactory : IAudioDeviceFactory
{
    internal WebAudioDevice? Device { get; private set; }

    /// <inheritdoc/>
    public async Task<IAudioDevice> CreateAsync()
    {
        Device = await WebAudioDevice.CreateAsync("./lumyte-audio.js");
        return Device;
    }
}
