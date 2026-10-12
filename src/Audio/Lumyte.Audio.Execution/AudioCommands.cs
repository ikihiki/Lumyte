using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>Typed commands operating on real backend instances on their creation thread.</summary>
public sealed partial class AudioCommands
{
    private readonly IAudioDeviceFactory _factory;
    private readonly HashSet<AudioClip> _clips = [];
    private readonly HashSet<AudioVoice> _voices = [];
    private IAudioDevice? _device;
    private Task<IAudioDevice>? _pending;
    private int _ownerThreadId;

    internal AudioCommands(IAudioDeviceFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Gets the thread on which backend creation was started.</summary>
    public int OwnerThreadId => Volatile.Read(ref _ownerThreadId);

    private IAudioDevice Device => _device ?? throw new InvalidOperationException("Audio is not initialized.");

    [ExecutionCommand(AllowAwaitableResult = true)]
    private Task<IAudioDevice> BeginCreate()
    {
        Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);
        return _pending = _factory.CreateAsync();
    }

    [ExecutionCommand]
    private void Attach(IAudioDevice device)
    {
        _device = device;
        _pending = null;
    }

    [ExecutionCommand]
    private AudioClip CreateClip(AudioFormat format, ReadOnlyMemory<float> samples)
    {
        var clip = new AudioClip(this, Device.CreateClip(format, samples.Span));
        _clips.Add(clip);
        return clip;
    }

    [ExecutionCommand]
    private AudioVoice CreateVoice(AudioClip clip, AudioBus bus)
    {
        VerifyClip(clip);
        var voice = new AudioVoice(this, Device.CreateVoice(clip.Backend, bus));
        _voices.Add(voice);
        return voice;
    }

    [ExecutionCommand(Sender = "Clip")]
    private void DestroyClip([ExecutionTarget] AudioClip clip)
    {
        VerifyClip(clip);
        clip.Backend.Dispose();
        _clips.Remove(clip);
    }

    [ExecutionCommand(Sender = "Voice")]
    private void Play([ExecutionTarget] AudioVoice voice)
    {
        VerifyVoice(voice);
        voice.Backend.Play();
    }

    [ExecutionCommand(Sender = "Voice")]
    private void Pause([ExecutionTarget] AudioVoice voice)
    {
        VerifyVoice(voice);
        voice.Backend.Pause();
    }

    [ExecutionCommand(Sender = "Voice")]
    private void Stop([ExecutionTarget] AudioVoice voice)
    {
        VerifyVoice(voice);
        voice.Backend.Stop();
    }

    [ExecutionCommand(Sender = "Voice")]
    private void SetGain([ExecutionTarget] AudioVoice voice, float gain)
    {
        VerifyVoice(voice);
        voice.Backend.Gain = gain;
    }

    [ExecutionCommand(Sender = "Voice")]
    private void SetLoop([ExecutionTarget] AudioVoice voice, bool loop)
    {
        VerifyVoice(voice);
        voice.Backend.Loop = loop;
    }

    [ExecutionCommand(Sender = "Voice")]
    private AudioPlaybackState ReadState([ExecutionTarget] AudioVoice voice)
    {
        VerifyVoice(voice);
        return voice.Backend.State;
    }

    [ExecutionCommand(Sender = "Voice")]
    private void Destroy([ExecutionTarget] AudioVoice voice)
    {
        VerifyVoice(voice);
        voice.Backend.Dispose();
        _voices.Remove(voice);
    }

    [ExecutionCommand]
    private void SetBusGain(AudioBus bus, float gain) => Device.SetBusGain(bus, gain);

    [ExecutionCommand]
    private void Refresh() => Device.Update();

    [ExecutionUpdate]
    private void Update(in UpdateTick tick) => Device.Update();

    [ExecutionCommand]
    private void Close()
    {
        foreach (AudioVoice voice in _voices.ToArray())
        {
            voice.Backend.Dispose();
            _voices.Remove(voice);
        }

        foreach (AudioClip clip in _clips.ToArray())
        {
            clip.Backend.Dispose();
            _clips.Remove(clip);
        }

        IAudioDevice? device = _device ?? (_pending?.IsCompletedSuccessfully == true ? _pending.Result : null);
        device?.Dispose();
        _device = null;
        _pending = null;
    }

    private void VerifyVoice(AudioVoice voice)
    {
        if (voice.Owner != this || !_voices.Contains(voice))
        {
            throw new InvalidOperationException("Foreign or destroyed voice.");
        }
    }

    private void VerifyClip(AudioClip clip)
    {
        if (clip.Owner != this || !_clips.Contains(clip))
        {
            throw new InvalidOperationException("Foreign or destroyed clip.");
        }
    }
}
