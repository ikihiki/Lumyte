using System.Reflection;
using BenchmarkDotNet.Attributes;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop.Benchmark.HostCompletedFrame;
using Xunit;
using Xunit.Abstractions;

namespace Lumyte.UI.Benchmark.Tests;

/// <summary>Validates exact completion joins independently of graphics and gates genuine native presentation coverage.</summary>
/// <param name="output">The xUnit output captured in retained TRX proof artifacts.</param>
public sealed class DesktopApplicationBenchmarkTests(ITestOutputHelper output)
{
    /// <summary>Checks stale in-flight completion, foreign runs/sources and unassociated IDs cannot satisfy a request.</summary>
    /// <returns>The deterministic acknowledgment test.</returns>
    [Fact]
    public async Task CompletionRequiresNewAcceptedMatchingFrameAsync()
    {
        using var listener = new FrameAcknowledgment();
        var run = Guid.NewGuid();
        var source = Guid.NewGuid();
        listener.Observe(true, run, 1, source);
        Task<long> completion = listener.Arm();
        listener.Observe(false, run, 1, source, presented: true);
        listener.Observe(true, Guid.Empty, 0, source);
        Assert.False(completion.IsCompleted);
        listener.Observe(true, run, 2, source);
        listener.Observe(false, run, 1, source, presented: true);
        listener.Observe(false, Guid.NewGuid(), 2, source, presented: true);
        listener.Observe(false, run, 2, Guid.NewGuid(), presented: true);
        Assert.False(completion.IsCompleted);
        listener.Observe(false, run, 2, source, presented: true);
        Assert.Equal(2, await completion);
        Task<long> next = listener.Arm();
        listener.Observe(false, run, 2, source, presented: true);
        Assert.False(next.IsCompleted);
        listener.Observe(true, run, 3, source);
        listener.Observe(false, run, 3, source, presented: true);
        Assert.Equal(3, await next);
    }

    /// <summary>Proves a concurrent application owner cannot select the native host request's frame.</summary>
    /// <returns>The deterministic thread-identity acknowledgment check.</returns>
    [Fact]
    public async Task ForeignOwnerCannotSelectAcceptedFrameAsync()
    {
        using var listener = new FrameAcknowledgment();
        Task<long> completion = listener.Arm();
        var run = Guid.NewGuid();
        var source = Guid.NewGuid();
        var otherOwner = new Thread(() => listener.Observe(true, run, 1, source));
        otherOwner.Start();
        otherOwner.Join();
        listener.Observe(false, run, 1, source, presented: true);
        Assert.False(completion.IsCompleted);
        listener.Observe(true, run, 2, source);
        listener.Observe(false, run, 2, source, presented: true);
        Assert.Equal(2, await completion);
    }

    /// <summary>Rejects overlapping serial requests and failed presentation instead of counting success.</summary>
    /// <returns>The deterministic failure test.</returns>
    [Fact]
    public async Task FailedFrameDoesNotBecomeSuccessfulSampleAsync()
    {
        using var listener = new FrameAcknowledgment();
        Task<long> completion = listener.Arm();
        Assert.Throws<InvalidOperationException>(() => { _ = listener.Arm(); });
        var run = Guid.NewGuid();
        var source = Guid.NewGuid();
        listener.Observe(true, run, 1, source);
        listener.Observe(false, run, 1, source, outcome: 3);
        await Assert.ThrowsAsync<InvalidOperationException>(() => completion);
        Task<long> recovered = listener.Arm();
        listener.Observe(true, run, 2, source, generation: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovered);
    }

    /// <summary>Rejects enumeration, CPU fallback and missing actual adapter identity without native initialization.</summary>
    [Fact]
    public void NativeAuditRequiresActualSelectedHardwareLine()
    {
        Assert.False(NativeAdapterAudit.IsHardwareSelection("* AdapterInfo { device_type: IntegratedGpu }"));
        Assert.False(NativeAdapterAudit.IsHardwareSelection("Request adapter result AdapterInfo { device_type: Cpu }"));
        Assert.False(NativeAdapterAudit.IsHardwareSelection("Request adapter result AdapterInfo { name: lavapipe, device_type: IntegratedGpu }"));
        Assert.True(NativeAdapterAudit.IsHardwareSelection("Request adapter result AdapterInfo { name: hardware, device_type: IntegratedGpu }"));
    }

    /// <summary>Checks the bounded eight-case contract and purpose category without constructing an application.</summary>
    [Fact]
    public void DesktopCasesAreBoundedAndExplicitlyIntegrated()
    {
        Type type = typeof(DesktopFrameBenchmarks);
        Assert.Equal(4, type.GetMethods().Count(method => method.IsDefined(typeof(BenchmarkAttribute))));
        Assert.Equal(new object[] { 100, 1000 }, type.GetProperty(nameof(DesktopFrameBenchmarks.ItemCount))!.GetCustomAttribute<ParamsAttribute>()!.Values);
        Assert.Equal(new[] { "Screening", "Comparison", "Diagnostic" }, type.GetCustomAttribute<BenchmarkCategoryAttribute>()!.Categories);
        Assert.Throws<InvalidDataException>(() => PresentationPixels.Verify(new byte[33], new[] { false }));
    }

    /// <summary>Exercises static, local and full actual standard desktop frames with untimed analytic pixels and orderly stop.</summary>
    /// <param name="count">The approved visible rectangle count.</param>
    /// <returns>The explicitly opted-in native functional validation, never a performance run.</returns>
    [OwnedDesktopTheory]
    [InlineData(100)]
    [InlineData(1000)]
    public async Task StandardDesktopCompletesActualMutatedFramesAsync(int count)
    {
        await using var fixture = new DesktopFrameFixture(count);
        try
        {
            await fixture.StartAsync();
        }
        catch (Exception error)
        {
            output.WriteLine($"RectangleCount={count}; startup exception: {error}; original host failure: {fixture.Failure}");
            throw;
        }

        output.WriteLine(fixture.AdapterIdentity);
        output.WriteLine($"Actual target={fixture.PixelWidth}x{fixture.PixelHeight}; requested=320x400; scale=1.");
        long first = await fixture.CompleteAsync(0);
        long local = await fixture.CompleteAsync(1);
        Assert.True(local > first);
        await fixture.VerifyPixelsAsync();
        long full = await fixture.CompleteAsync(count);
        Assert.True(full > local);
        await fixture.VerifyPixelsAsync();
        long layout = await fixture.CompleteAsync(0, localLayout: true);
        Assert.True(layout > full);
        output.WriteLine($"RectangleCount={count}; successful matching frames: static={first}, localPaint={local}, fullDirty={full}, localLayout={layout}.");
        await fixture.VerifyPixelsAsync();
        await fixture.DisposeAsync();
        Assert.Equal(UiApplicationState.Stopped, fixture.State);
    }

    /// <summary>Reports an actual xUnit skip when explicit owned-display/hardware prerequisites are absent.</summary>
    private sealed class OwnedDesktopTheoryAttribute : TheoryAttribute
    {
        /// <summary>Initializes a new instance of the <see cref="OwnedDesktopTheoryAttribute"/> class.</summary>
        public OwnedDesktopTheoryAttribute()
        {
            Skip = DesktopFrameFixture.UnavailableReason();
        }
    }
}
