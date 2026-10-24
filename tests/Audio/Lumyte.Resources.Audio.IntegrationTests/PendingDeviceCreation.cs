using Lumyte.Audio.Abstractions;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal sealed class PendingDeviceCreation
{
    internal TaskCompletionSource<TestDevice> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TaskCompletionSource<IAudioDevice> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Attach(TestDevice device) => Started.SetResult(device);

    internal void Complete() => Completion.SetResult(Started.Task.GetAwaiter().GetResult());
}
