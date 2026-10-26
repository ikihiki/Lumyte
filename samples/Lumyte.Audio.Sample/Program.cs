using Lumyte.Audio.Abstractions;
using Lumyte.Audio.OpenAL;
using Lumyte.Audio.Samples;

using var device = new OpenALDevice(maxVoices: 2);
using var other = new OpenALDevice(maxVoices: 2);
AudioExercise.Run(device, other);
Task.Run(() =>
{
    try
    {
        device.Update();
    }
    catch (InvalidOperationException)
    {
        return;
    }

    throw new InvalidOperationException("Wrong-thread access was accepted.");
}).GetAwaiter().GetResult();
using IAudioClip clip = device.CreateClip(new(48000, 1), AudioExercise.Tone(4800));
using IAudioVoice voice = device.CreateVoice(clip);
voice.Play();
Thread.Sleep(250);
device.Update();
AudioExercise.Equal(voice.State == AudioPlaybackState.Completed, "Natural completion");
voice.Play();
voice.Loop = true;
Thread.Sleep(250);
device.Update();
AudioExercise.Equal(voice.State == AudioPlaybackState.Playing, "Loop across clip boundary");
voice.Stop();
Console.WriteLine("OpenAL audio contracts passed.");
