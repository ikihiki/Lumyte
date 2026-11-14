using Lumyte.Input.Processing.Benchmark;
using Xunit;

namespace Lumyte.Input.Benchmark.Tests;

/// <summary>Public-API benchmark workload oracles.</summary>
public sealed class HistoryAndProcessingTests
{
    /// <summary>Checks deterministic workload results.</summary>
    /// <param name="batch">Samples per device.</param>
    /// <param name="devices">Physical device count.</param>
    /// <param name="windows">Window scope count.</param>
    [Theory]
    [InlineData(16, 1, 1)]
    [InlineData(128, 4, 2)]
    public void HistoryWavesRetainActualCountsAndStableSnapshots(int batch, int devices, int windows)
    {
        var benchmark = new HistoryBenchmarks { BatchSize = batch, DeviceCount = devices, WindowCount = windows };
        HistoryWave manual = benchmark.OrderedIngestionAndSnapshots();
        Assert.Equal(devices * (1 + (2 * (windows + batch))), manual.Retained);
        Assert.Equal(0, manual.Gaps);
        Assert.True(manual.OriginScopes);
        Assert.True(manual.OrderedCaptures);
        Assert.Equal(manual.Tail, manual.ReadCursor);

        // A physical key's series remains owned by its first window even if later

        // repeats arrive from another window. Arrival window is not processing scope.
        Assert.Equal(devices, manual.PressedScopes);
        Assert.Equal(1 + windows + batch, manual.FrozenCount);
        Assert.Equal(manual.FrozenCount, manual.FrozenCountAfterUpdate);
        Assert.Equal((ulong)manual.Retained, manual.Tail);
        Assert.Equal(manual, benchmark.OrderedIngestionAndSnapshots());
        HistoryWave counted = benchmark.CountRetentionAndReadGap();
        Assert.Equal(devices * batch / 2, counted.Retained);
        Assert.Equal(devices, counted.Gaps);
        Assert.Equal(devices, counted.PressedScopes);
        HistoryWave aged = benchmark.TimeRetentionAndReadGap();
        Assert.Equal(devices * (windows + batch), aged.Retained);
        Assert.Equal(devices, aged.Gaps);
        Assert.Equal(devices, aged.PressedScopes);
        HistoryWave canceled = benchmark.NotificationsAndFocusCancellation();
        Assert.Equal(0, canceled.PressedScopes);
        Assert.Equal(canceled.Retained, canceled.Notifications);
        Assert.Equal(devices * (1 + (3 * windows) + (2 * batch)), canceled.Retained);
    }

    /// <summary>Checks deterministic workload results.</summary>
    /// <param name="batch">Sample count.</param>
    /// <param name="windows">Window scope count.</param>
    [Theory]
    [InlineData(8, 1)]
    [InlineData(64, 2)]
    public void CorrectionPrecedesTimeBasedSmoothingAndEmptyTicksAdvanceIt(int batch, int windows)
    {
        var benchmark = new ProcessingBenchmarks { BatchSize = batch, WindowCount = windows };
        ProcessingWave calibrated = benchmark.CalibratedAnalog();
        Assert.Equal(0.5f, calibrated.Value, 5);
        Assert.Equal(1 + windows + (2 * batch), calibrated.Records);
        Assert.Equal(Enumerable.Range(1, windows + (2 * batch)).Select(i => (ulong)i), calibrated.Captures);
        ProcessingWave smoothed = benchmark.CalibratedAndSmoothedAnalog();
        Assert.Equal(0.5f * (1 - MathF.Exp(-2)), smoothed.Value, 5);

        // The update after the second raw wave emits a zero-delta clock sample,

        // and the next empty update emits the advancing sample, per scope.
        Assert.Equal(calibrated.Records + (2 * windows), smoothed.Records);
        Assert.Equal(calibrated.Captures, smoothed.Captures);
        Assert.Equal(smoothed.Value, benchmark.CalibratedAndSmoothedAnalog().Value);
    }

    /// <summary>Checks deterministic workload results.</summary>
    /// <param name="batch">Sample count.</param>
    /// <param name="windows">Window scope count.</param>
    [Theory]
    [InlineData(8, 1)]
    [InlineData(64, 2)]
    public void TouchCorrectionAndVirtualizationKeepCausalOrderAndFinishNeutral(int batch, int windows)
    {
        var benchmark = new ProcessingBenchmarks { BatchSize = batch, WindowCount = windows };
        ProcessingWave wave = benchmark.TouchGesturePipeline();
        Assert.Equal(0.25f, wave.Value);
        Assert.Equal(batch, wave.Swipes);
        Assert.Equal(0, wave.Contacts);
        Assert.Equal(2 + (3 * windows) + (11 * batch), wave.Records);
        Assert.Equal(wave.Captures.Order(), wave.Captures);
        Assert.Equal(windows + (8 * batch), wave.Captures.Length);
        Assert.Equal(wave.Records, benchmark.TouchGesturePipeline().Records);
    }
}
