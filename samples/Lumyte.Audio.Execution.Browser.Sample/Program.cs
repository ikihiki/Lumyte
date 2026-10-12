using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution.Browser.Sample;

[SupportedOSPlatform("browser")]
internal static partial class Program
{
    private static readonly BrowserDeviceFactory _factory = new();
    private static readonly AudioModule _audio = new(_factory);
    private static readonly ExecutionRuntime _runtime = new([_audio]);
    private static bool _resumePending;
    private static bool _finished;
    private static string _report = string.Empty;

    [JSExport]
    internal static string GetReport() => _report;

    [JSExport]
    internal static async Task RunAsync()
    {
        try
        {
            // Start resume directly during the gesture, before yielding or queueing commands.
            _resumePending = true;
            await _factory.Device!.ResumeOutputAsync();
            _resumePending = false;
            float[] pcm = new float[4800];
            for (int i = 0; i < pcm.Length; i++)
            {
                pcm[i] = (float)(0.1 * Math.Sin(i * 2 * Math.PI * 440 / 48000));
            }

            var clip = new AudioClip(await _audio.State.CreateClip(new AudioFormat(48000, 1), pcm));
            IAudioVoice original = await clip.CreateVoice(AudioBus.Effects);
            var voice = new AudioVoice(original);
            await voice.Play();
            await voice.Pause();
            await _audio.State.Refresh();
            if (original.State != AudioPlaybackState.Paused)
            {
                throw new InvalidOperationException("Browser voice is not paused.");
            }

            await voice.SetLoop(true);
            await voice.Play();
            await voice.Stop();
            await voice.Destroy();
            await clip.DestroyClip();
            _report = "WebAudio typed commands passed on the single Host thread.";
        }
        finally
        {
            _resumePending = false;
            await _runtime.StopAsync();
            _finished = true;
        }
    }

    private static async Task Main()
    {
        _runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Task startup = _runtime.StartAsync().AsTask();
        while (!startup.IsCompleted)
        {
            _runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            await Task.Delay(1);
        }

        await startup;
        _ = PumpAsync();
    }

    private static async Task PumpAsync()
    {
        while (!_finished)
        {
            _runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            if (!_resumePending && _runtime.Status is ExecutionStatus.Running or ExecutionStatus.Quiescing)
            {
                _runtime.RunHostPhase(_audio.HostPhase, 64, TimeSpan.FromMilliseconds(2));
            }

            await Task.Delay(1);
        }
    }
}
