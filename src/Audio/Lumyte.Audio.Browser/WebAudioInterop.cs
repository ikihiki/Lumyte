using System.Runtime.InteropServices.JavaScript;

namespace Lumyte.Audio.Browser;

internal static partial class WebAudioInterop
{
    [JSImport("createDevice", "Lumyte.Audio.Browser")]
    internal static partial JSObject CreateDevice();

    [JSImport("outputState", "Lumyte.Audio.Browser")]
    internal static partial int OutputState(JSObject device);

    [JSImport("resume", "Lumyte.Audio.Browser")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    internal static partial Task ResumeAsync(JSObject device);

    [JSImport("destroyDevice", "Lumyte.Audio.Browser")]
    internal static partial void DestroyDevice(JSObject device);

    [JSImport("createClip", "Lumyte.Audio.Browser")]
    internal static partial JSObject CreateClip(JSObject device, int rate, int channels, [JSMarshalAs<JSType.MemoryView>] Span<byte> samples);

    [JSImport("createVoice", "Lumyte.Audio.Browser")]
    internal static partial JSObject CreateVoice(JSObject device, JSObject clip);

    [JSImport("play", "Lumyte.Audio.Browser")]
    internal static partial void Play(JSObject voice);

    [JSImport("pause", "Lumyte.Audio.Browser")]
    internal static partial void Pause(JSObject voice);

    [JSImport("stop", "Lumyte.Audio.Browser")]
    internal static partial void Stop(JSObject voice);

    [JSImport("completed", "Lumyte.Audio.Browser")]
    internal static partial bool Completed(JSObject voice);

    [JSImport("setGain", "Lumyte.Audio.Browser")]
    internal static partial void SetGain(JSObject voice, float gain);

    [JSImport("setLoop", "Lumyte.Audio.Browser")]
    internal static partial void SetLoop(JSObject voice, bool loop);

    [JSImport("destroyVoice", "Lumyte.Audio.Browser")]
    internal static partial void DestroyVoice(JSObject voice);
}
