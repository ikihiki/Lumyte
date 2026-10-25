using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Execution;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal static class RecoveryScenario
{
    internal static void Run(AudioModule audio, ExecutionRuntime execution, ResourceManager manager, TestFactory factory, Action<Task> wait)
    {
        Task<ResourceReference<IAudioVoice>> load = manager.AcquireAsync(new ResourceKey<IAudioVoice>("recovery")).AsTask();
        wait(load);
        using ResourceReference<IAudioVoice> reference = load.GetAwaiter().GetResult();
        using ResourceLease<IAudioVoice> old = reference.Acquire();
        var oldVoice = (TestVoice)old.Value;
        TestDevice oldDevice = factory.Current;
        AudioDeviceSnapshot initial = audio.Devices.State;

        oldDevice.SetOutput(AudioOutputState.Suspended);
        wait(audio.State.Refresh().AsTask());
        Require(audio.Devices.State == initial && old.IsUsable, "Suspension invalidated the device generation.");
        oldDevice.SetOutput(AudioOutputState.Running);
        wait(audio.State.Refresh().AsTask());
        Require(audio.Devices.State == initial, "Resuming output changed the device generation.");

        oldDevice.SetOutput(AudioOutputState.Unavailable);
        wait(audio.State.Refresh().AsTask());
        AudioDeviceSnapshot lost = audio.Devices.State;
        Require(lost.Status == AudioDeviceServiceStatus.Lost && lost.InvalidatedThroughVersion >= initial.Version, "Device loss did not invalidate the Ready version.");
        Require(!old.IsUsable && execution.Status == ExecutionStatus.Running, "Loss did not remain local to the Audio device.");
        Expect<ResourceUnavailableException>(() => _ = old.Value, "Resources returned an invalidated audio generation.");
        Expect<AudioDeviceUnavailableException>(() => wait(new AudioVoice(oldVoice).Play().AsTask()), "An old voice accepted playback after device loss.");

        factory.FailNextCreation();
        Expect<InvalidOperationException>(() => wait(audio.Devices.RecoverAsync().AsTask()), "Injected recovery failure was ignored.");
        Require(audio.Devices.State.Status == AudioDeviceServiceStatus.Failed && execution.Status == ExecutionStatus.Running, "Recovery failure stopped the execution runtime or was not recorded.");
        wait(audio.Devices.RecoverAsync().AsTask());
        AudioDeviceSnapshot recovered = audio.Devices.State;
        Require(recovered.Status == AudioDeviceServiceStatus.Ready && recovered.Epoch > initial.Epoch && recovered.Version > lost.Version, "Recovery did not publish a new Ready generation.");
        Require(!old.IsUsable, "Recovery made a lost generation usable again.");
        Task<AudioDeviceLease> acquire = audio.Devices.AcquireAsync().AsTask();
        wait(acquire);
        using (AudioDeviceLease freshDevice = acquire.GetAwaiter().GetResult())
        {
            Expect<ArgumentException>(() => wait(audio.State.CreateVoiceForLease(freshDevice, oldVoice.Clip, AudioBus.Effects).AsTask()), "An old clip was relabeled as belonging to the new device generation.");
        }

        Expect<ArgumentException>(() => wait(audio.State.CreateVoiceWithLease(oldVoice.Clip, AudioBus.Effects).AsTask()), "Combined voice creation accepted a clip from another generation.");

        Task<ResourceReloadResult> rebuild = manager.RebuildRuntimeAsync(["Audio"]).AsTask();
        wait(rebuild);
        Require(rebuild.GetAwaiter().GetResult().Status == ResourceReloadStatus.Published, "Audio recovery did not publish rebuilt resources.");
        using ResourceLease<IAudioVoice> current = reference.Acquire();
        var currentVoice = (TestVoice)current.Value;
        Require(!ReferenceEquals(oldVoice, currentVoice) && ReferenceEquals(currentVoice.Device, factory.Current), "Rebuilt voice did not use the new device.");
        Require(currentVoice.State == AudioPlaybackState.Stopped, "Recovery unexpectedly restarted playback.");
        wait(new AudioVoice(currentVoice).Play().AsTask());
        Require(currentVoice.State == AudioPlaybackState.Playing, "Rebuilt voice could not play.");

        Task<int> retained = audio.Devices.RetireAsync().AsTask();
        wait(retained);
        Require(retained.GetAwaiter().GetResult() == 0 && !oldDevice.IsDisposed, "An old device was disposed while its resource lease remained held.");
        old.Dispose();
        oldDevice.FailNextVoiceRelease();
        Expect<AggregateException>(() => wait(manager.TrimAsync().AsTask()), "Injected native cleanup failure was ignored.");
        Task<int> failedRelease = audio.Devices.RetireAsync().AsTask();
        wait(failedRelease);
        Require(failedRelease.GetAwaiter().GetResult() == 0 && !oldDevice.IsDisposed, "Failed native cleanup released the device generation lease.");
        wait(manager.TrimAsync().AsTask());
        Task<int> retired = audio.Devices.RetireAsync().AsTask();
        wait(retired);
        Require(retired.GetAwaiter().GetResult() == 1 && oldDevice.IsDisposed, "The old generation was not released after native children and leases.");
        Require(current.IsUsable && !factory.Current.IsDisposed, "Retiring the old device affected the new generation.");
        Console.WriteLine("Resource audio recovery passed: suspension, loss, failed recovery retry, generation rebuild, mismatched clip rejection, and cleanup retry.");
    }

    internal static void StopDuringRecovery(AudioModule audio, ExecutionRuntime execution, TestFactory factory, Action<Task> wait)
    {
        factory.Current.SetOutput(AudioOutputState.Unavailable);
        wait(audio.State.Refresh().AsTask());
        PendingDeviceCreation pending = factory.DelayNextCreation();
        Task<AudioDeviceSnapshot> recovery = audio.Devices.RecoverAsync().AsTask();
        Task<AudioDeviceSnapshot> shared = audio.Devices.RecoverAsync().AsTask();
        wait(pending.Started.Task);
        TestDevice candidate = pending.Started.Task.GetAwaiter().GetResult();
        Task stop = execution.StopAsync().AsTask();
        try
        {
            Require(!stop.IsCompleted && !candidate.IsDisposed, "Shutdown completed before the pending device factory.");
        }
        finally
        {
            pending.Complete();
            wait(stop);
        }

        ObserveStoppedRecovery(recovery, wait);
        ObserveStoppedRecovery(shared, wait);
        Require(candidate.IsDisposed && execution.Status == ExecutionStatus.Stopped, "Shutdown did not release the asynchronously created candidate on its owner.");
        Console.WriteLine("Resource audio recovery shutdown passed: concurrent recovery waits, pending factory completion, and owner cleanup.");
    }

    internal static void CancelBeforeOwnerStart(AudioModule audio, ExecutionRuntime execution, TestFactory factory, Action<Task> wait)
    {
        using var cancellation = new CancellationTokenSource();
        Task start = execution.StartAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        Expect<OperationCanceledException>(() => wait(start), "Startup ignored cancellation before the first owner pump.");
        wait(execution.StopAsync().AsTask());
        Require(factory.CreationCount == 0 && audio.Devices.State.Status == AudioDeviceServiceStatus.Stopped, "Canceling queued startup did not clean up the uninitialized audio module.");
        Console.WriteLine("Resource audio canceled startup passed: no factory invocation and clean owner shutdown.");
    }

    internal static void LossDuringBuild(AudioModule audio, ExecutionRuntime execution, ResourceManager manager, TestFactory factory, Action<Task> wait)
    {
        TestDevice device = factory.Current;
        int created = device.CreatedClips;
        int returned = device.ReturnedClips;
        device.LoseDuringNextClip();
        Task<ResourceReference<IAudioVoice>> build = manager.AcquireAsync(new ResourceKey<IAudioVoice>("loss-during-build")).AsTask();
        try
        {
            wait(build);
            using ResourceReference<IAudioVoice> unexpected = build.GetAwaiter().GetResult();
            throw new InvalidOperationException("A candidate built across device loss was published.");
        }
        catch (AudioDeviceUnavailableException)
        {
        }
        catch (ResourceUnavailableException)
        {
        }

        Require(device.CreatedClips == created + 1 && device.ReturnedClips == returned + 1, "The unpublished candidate clip was not released.");
        Require(execution.Status == ExecutionStatus.Running && audio.Devices.State.Status == AudioDeviceServiceStatus.Lost, "Loss during resource creation escaped Audio recovery.");
        wait(audio.Devices.RecoverAsync().AsTask());
        wait(manager.TrimAsync().AsTask());
        wait(audio.Devices.RetireAsync().AsTask());
        Require(device.IsDisposed, "The lost build generation remained owned after all candidate cleanup.");
        Console.WriteLine("Resource audio build-loss passed: rejected publication and released the candidate on its old owner.");
    }

    internal static void RetryCapacityFailure(AudioModule audio, ExecutionRuntime execution, TestFactory factory, Action<Task> wait, Action<Func<bool>> pumpUntil)
    {
        factory.Current.SetOutput(AudioOutputState.Unavailable);
        wait(audio.State.Refresh().AsTask());
        PendingDeviceCreation pending = factory.DelayNextCreation();
        Task<AudioDeviceSnapshot> recovery = audio.Devices.RecoverAsync().AsTask();
        wait(pending.Started.Task);
        TestDevice candidate = pending.Started.Task.GetAwaiter().GetResult();
        int creations = factory.CreationCount;
        var retained = new ValueTask<Unit>[16];
        int accepted = 0;
        pumpUntil(() =>
        {
            while (accepted < retained.Length)
            {
                ValueTask<Unit> command = audio.State.Refresh();
                if (command.IsFaulted)
                {
                    try
                    {
                        command.GetAwaiter().GetResult();
                    }
                    catch (ExecutionQueueFullException)
                    {
                        // BeginRecovery may still hold its result slot after the factory signals Started.
                        return false;
                    }
                }

                retained[accepted++] = command;
            }

            return true;
        });

        pumpUntil(() => retained.All(command => command.IsCompleted));
        try
        {
            pending.Complete();
            Expect<ExecutionQueueFullException>(() => wait(recovery), "Recovery completion ignored retained command capacity.");
        }
        finally
        {
            foreach (ValueTask<Unit> command in retained)
            {
                command.GetAwaiter().GetResult();
            }
        }

        wait(audio.Devices.RecoverAsync().AsTask());
        Require(factory.CreationCount == creations && ReferenceEquals(factory.Current, candidate) && !candidate.IsDisposed, "Retry replaced or leaked the already created recovery candidate.");
        Require(audio.Devices.State.Status == AudioDeviceServiceStatus.Ready && execution.Status == ExecutionStatus.Running, "Capacity retry did not restore the audio device.");
        wait(audio.Devices.RetireAsync().AsTask());
        Console.WriteLine("Resource audio recovery capacity passed: retained candidate and retried completion without another factory call.");
    }

    private static void ObserveStoppedRecovery(Task recovery, Action<Task> wait)
    {
        try
        {
            wait(recovery);
        }
        catch (InvalidOperationException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void Expect<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
