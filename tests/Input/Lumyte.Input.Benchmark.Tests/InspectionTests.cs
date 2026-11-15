using System.Text.Json;
using Lumyte.Input.Diagnostics.Benchmark;
using Xunit;

namespace Lumyte.Input.Benchmark.Tests;

/// <summary>Independent assertions for inspection benchmark consumers.</summary>
public sealed class InspectionTests
{
    /// <summary>Checks detached JSON against known physical input.</summary>
    /// <param name="frames">The bounded frame count.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void SnapshotContainsObservedDeviceActionAndHistory(int frames)
    {
        InspectionWave result = new InspectionBenchmarks().CaptureDeviceAndActionViews(frames);
        Assert.Equal(Enumerable.Range(0, frames).Select(i => i % 2 == 0 ? 1f : 0f), result.Values);
        using var document = JsonDocument.Parse(result.Snapshot);
        JsonElement root = document.RootElement;
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(result.Revision, root.GetProperty("revision").GetInt64());
        JsonElement input = Assert.Single(root.GetProperty("inputSystems").EnumerateArray());
        Assert.Single(input.GetProperty("sources").EnumerateArray());
        JsonElement device = Assert.Single(input.GetProperty("devices").EnumerateArray());
        Assert.Equal("Keyboard", device.GetProperty("kind").GetString());
        Assert.Equal("inspection keyboard", device.GetProperty("name").GetString());
        Assert.True(device.GetProperty("connected").GetBoolean());
        Assert.True(device.GetProperty("focused").GetBoolean());
        Assert.True(ulong.Parse(device.GetProperty("sequence").GetString()!) > 0);
        Assert.True(Guid.TryParse(device.GetProperty("targets").GetProperty("Key.A").GetString(), out _));
        Assert.Equal(frames % 2, device.GetProperty("state").GetProperty("keys").GetArrayLength());
        JsonElement history = Assert.Single(device.GetProperty("history").EnumerateArray());
        Assert.Equal("KeyData", history.GetProperty("type").GetString());
        JsonElement system = Assert.Single(root.GetProperty("actionSystems").EnumerateArray());
        Assert.Equal(frames * 10, system.GetProperty("timeMs").GetInt32());
        JsonElement context = Assert.Single(system.GetProperty("contexts").EnumerateArray());
        Assert.Equal("game", context.GetProperty("id").GetString());
        JsonElement action = Assert.Single(context.GetProperty("actions").EnumerateArray());
        Assert.Equal("jump", action.GetProperty("id").GetString());
        Assert.Equal(result.Values[^1], action.GetProperty("value").GetProperty("x").GetSingle());
        Assert.True(Guid.TryParse(action.GetProperty("target").GetString(), out _));
        Assert.Single(action.GetProperty("bindings").EnumerateArray());
    }

    /// <summary>Checks effective state, expiry and rejection codes.</summary>
    [Fact]
    public void LeaseTransitionsChangeRealActionValues()
    {
        var benchmark = new InspectionBenchmarks();
        InspectionWave lifecycle = benchmark.AcquireUpdateRelease();
        Assert.Equal(new float[] { 1, 0, 0 }, lifecycle.Values);
        Assert.Equal(3, lifecycle.Accepted);
        InspectionWave expiry = benchmark.ExpireOnLogicalTime();
        Assert.Equal(new float[] { 1, 0 }, expiry.Values);
        Assert.Equal(1, expiry.Expired);
        Assert.Equal("lease-not-found", expiry.Error);
        InspectionWave rejection = benchmark.RejectBusyAndStaleRevision();
        Assert.Equal(new float[] { 1 }, rejection.Values);
        Assert.Equal(2, rejection.Rejected);
        Assert.Equal("target-busy/revision-conflict", rejection.Error);
        InspectionWave device = benchmark.OverrideDeviceAndRestorePhysical();
        Assert.Equal(new float[] { 1, 0, 1 }, device.Values);
        Assert.Equal(2, device.Accepted);
        using var document = JsonDocument.Parse(device.Snapshot);
        Assert.Equal("A", document.RootElement.GetProperty("inputSystems")[0].GetProperty("devices")[0].GetProperty("state").GetProperty("keys")[0].GetString());
    }

    /// <summary>Checks deterministic playback and isolated operation lifetimes.</summary>
    /// <param name="frames">The bounded frame count.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void TapeAndLiveAreBoundedAndRepeatable(int frames)
    {
        var benchmark = new InspectionBenchmarks();

        // Many complete invocations must not accumulate old operations or exceed capacity.
        for (int i = 0; i < 70; i++)
        {
            InspectionWave tape = benchmark.PlayFrameSequence();
            Assert.Equal(new float[] { 1, 0 }, tape.Values);
            Assert.Equal(3, tape.Commands);
            Assert.Equal("completed", tape.StopReason);
            InspectionWave live = benchmark.SendOrderedLiveSequence(frames);
            Assert.Equal(Enumerable.Range(0, frames).Select(n => n % 2 == 0 ? 1f : 0f).Append(0), live.Values);
            Assert.Equal(frames + 2, live.Accepted);
        }
    }
}
