using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Samples;

namespace Lumyte.Audio.Browser.Sample;

[SupportedOSPlatform("browser")]
internal static partial class Program
{
    private static WebAudioDevice? _device;
    private static string _report = string.Empty;

    [JSExport]
    internal static string GetReport() => _report;

    [JSExport]
    internal static async Task RunAsync()
    {
        WebAudioDevice device = _device ?? throw new InvalidOperationException("Device not initialized.");
        try
        {
            await device.ResumeOutputAsync();
            using WebAudioDevice other = await WebAudioDevice.CreateAsync("./lumyte-audio.js", 2);
            AudioExercise.Run(device, other);
            using IAudioClip clip = device.CreateClip(new(48000, 1), AudioExercise.Tone(4800));
            using IAudioVoice voice = device.CreateVoice(clip);
            voice.Play();
            await Task.Delay(250);
            device.Update();
            AudioExercise.Equal(voice.State == AudioPlaybackState.Completed, "Natural completion");
            voice.Loop = true;
            voice.Play();
            await Task.Delay(250);
            device.Update();
            AudioExercise.Equal(voice.State == AudioPlaybackState.Playing, "Loop across clip boundary");
            voice.Pause();
            device.Update();
            AudioExercise.Equal(voice.State == AudioPlaybackState.Paused, "Paused loop");
            voice.Play();
            voice.Stop();
            _report = "Web Audio .NET contracts passed.";
        }
        finally
        {
            device.Dispose();
        }
    }

    private static async Task Main() => _device = await WebAudioDevice.CreateAsync("./lumyte-audio.js", 2);
}
