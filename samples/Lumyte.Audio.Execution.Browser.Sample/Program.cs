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
            float[] pcm = new float[48000];
            for (int i = 0; i < pcm.Length; i++)
            {
                pcm[i] = (float)(0.1 * Math.Sin(i * 2 * Math.PI * 440 / 48000));
            }

            var clip = new AudioClip(await _audio.State.CreateClip(new AudioFormat(48000, 1), pcm));
            AudioVoice voice = await clip.CreateVoice(AudioBus.Effects);
            await voice.Seek(1200);
            await _audio.State.Refresh();
            if (voice.State != AudioPlaybackState.Stopped || voice.FramePosition != 1200)
            {
                throw new InvalidOperationException("Seeking the stopped Browser voice changed playback or lost its frame position.");
            }

            await voice.Play();
            await voice.Pause();
            await voice.Seek(2400);
            await _audio.State.Refresh();
            if (voice.State != AudioPlaybackState.Paused || voice.FramePosition != 2400)
            {
                throw new InvalidOperationException("Seeking the paused Browser voice changed playback or lost its frame position.");
            }

            await voice.SetLoop(true);
            await voice.Seek(pcm.Length);
            await _audio.State.Refresh();
            if (voice.State != AudioPlaybackState.Paused || voice.FramePosition != 0)
            {
                throw new InvalidOperationException("Seeking the looping Browser voice to its end did not wrap to frame zero.");
            }

            await voice.Play();
            await voice.Stop();
            await _audio.State.Refresh();
            if (voice.State != AudioPlaybackState.Stopped || voice.FramePosition != 0)
            {
                throw new InvalidOperationException("Stopping the Browser voice did not reset its frame position.");
            }

            await voice.Destroy();
            await clip.DestroyClip();
            _report = "WebAudio typed playback and Seek commands passed on the single Host thread.";
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
