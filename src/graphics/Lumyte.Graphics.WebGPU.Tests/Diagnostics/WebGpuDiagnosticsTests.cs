using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

public sealed class WebGpuDiagnosticsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MappingWaitsForBothDiagnosticResults(bool creationFinishesFirst)
    {
        var creation = Pending();
        var mapping = Pending();
        var status = new WebGpuDeviceStatus();
        Task completion = WebGpuDiagnostics.ObserveMapAsync(creation.Task, mapping.Task, status);

        (creationFinishesFirst ? creation : mapping).SetResult([]);

        Assert.False(completion.IsCompleted);
        (creationFinishesFirst ? mapping : creation).SetResult([]);
        await completion;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OperationFailureWaitsForTheOtherDiagnosticResult(bool creationFails)
    {
        var creation = Pending();
        var mapping = Pending();
        var diagnostic = new P.GpuDiagnostic(P.GpuDiagnosticKind.Validation, "invalid usage from runtime");
        Task completion = WebGpuDiagnostics.ObserveMapAsync(creation.Task, mapping.Task, new());

        (creationFails ? creation : mapping).SetResult([diagnostic]);

        Assert.False(completion.IsCompleted);
        (creationFails ? mapping : creation).SetResult([]);
        P.GpuOperationException failure = await Assert.ThrowsAsync<P.GpuOperationException>(() => completion);
        Assert.Equal(diagnostic, Assert.Single(failure.Diagnostics));
    }

    [Fact]
    public async Task CombinedDiagnosticsRetainBothRuntimeFailures()
    {
        var creationFailure = new P.GpuDiagnostic(P.GpuDiagnosticKind.Validation, "invalid buffer");
        var mappingFailure = new P.GpuDiagnostic(P.GpuDiagnosticKind.Internal, "mapping failed");
        var creation = Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>([creationFailure]);
        var mapping = Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>([mappingFailure]);

        P.GpuOperationException failure = await Assert.ThrowsAsync<P.GpuOperationException>(() =>
            WebGpuDiagnostics.ObserveMapAsync(creation, mapping, new()));

        Assert.Collection(failure.Diagnostics,
            item => Assert.Equal(creationFailure, item),
            item => Assert.Equal(mappingFailure, item));
    }

    [Fact]
    public async Task FailedObjectDoesNotFailAnUnrelatedOperation()
    {
        var status = new WebGpuDeviceStatus();
        var failed = Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>([
            new(P.GpuDiagnosticKind.OutOfMemory, "allocation failed")]);
        var success = Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>([]);

        await Assert.ThrowsAsync<P.GpuOperationException>(() =>
            WebGpuDiagnostics.ObserveAsync("CreateBuffer", failed, status));
        await WebGpuDiagnostics.ObserveAsync("CreateTexture", success, status);

        Assert.False(status.Failure.IsCompleted);
    }

    [Fact]
    public async Task SharedCreationFailureRemainsAvailableForEveryConsumer()
    {
        var creation = Pending();
        var mapping = Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>([]);
        var diagnostic = new P.GpuDiagnostic(P.GpuDiagnosticKind.Validation, "invalid shared buffer");
        var status = new WebGpuDeviceStatus();
        Task first = WebGpuDiagnostics.ObserveMapAsync(creation.Task, mapping, status);
        Task second = WebGpuDiagnostics.ObserveMapAsync(creation.Task, mapping, status);

        creation.SetResult([diagnostic]);

        P.GpuOperationException firstFailure = await Assert.ThrowsAsync<P.GpuOperationException>(() => first);
        P.GpuOperationException secondFailure = await Assert.ThrowsAsync<P.GpuOperationException>(() => second);
        Assert.Equal(diagnostic, Assert.Single(firstFailure.Diagnostics));
        Assert.Equal(diagnostic, Assert.Single(secondFailure.Diagnostics));
    }

    [Fact]
    public async Task DeviceLossEndsMappingWithUnresolvedDiagnostics()
    {
        var creation = Pending();
        var mapping = Pending();
        var status = new WebGpuDeviceStatus();
        Task completion = WebGpuDiagnostics.ObserveMapAsync(creation.Task, mapping.Task, status);

        status.Lose("lost during mapping");

        GpuDeviceLostException failure = await Assert.ThrowsAsync<GpuDeviceLostException>(() => completion);
        Assert.Contains("lost during mapping", failure.Message);
        Assert.False(creation.Task.IsCompleted);
        Assert.False(mapping.Task.IsCompleted);
    }

    [Fact]
    public async Task KnownDeviceLossOverridesSuccessfulDiagnostics()
    {
        var status = new WebGpuDeviceStatus();
        var success = Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>([]);
        status.Lose("device disconnected");

        GpuDeviceLostException failure = await Assert.ThrowsAsync<GpuDeviceLostException>(() =>
            WebGpuDiagnostics.ObserveAsync("CreateBuffer", success, status));

        Assert.Contains("device disconnected", failure.Message);
    }

    private static TaskCompletionSource<IReadOnlyList<P.GpuDiagnostic>> Pending()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
