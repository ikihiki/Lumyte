using System.Text.Json;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Actions;
using Lumyte.Input.Actions.Benchmark;
using Lumyte.Input.Settings;
using Lumyte.Input.Settings.Benchmark;
using Xunit;

namespace Lumyte.Input.Benchmark.Tests;

/// <summary>Asserts observable workload semantics without performance thresholds.</summary>
public sealed class ActionsAndSettingsTests
{
    /// <summary>Every bounded wave must recognize again rather than become a permanent no-op.</summary>
    /// <param name="kind">The algorithm.</param>
    /// <param name="expected">The completion count.</param>
    [Theory]
    [InlineData(RecognitionKind.Press, 2)]
    [InlineData(RecognitionKind.Hold, 1)]
    [InlineData(RecognitionKind.MultiTap, 1)]
    [InlineData(RecognitionKind.Chord, 1)]
    [InlineData(RecognitionKind.Sequence, 1)]
    public void RecognitionRearms(RecognitionKind kind, int expected)
    {
        var workload = new ActionBenchmarks { Recognition = kind };
        workload.Setup();
        try
        {
            Assert.Equal(expected, workload.RecognitionWave());
            Assert.Equal(expected, workload.RecognitionWave());
            Assert.Equal(expected, workload.ColdRecognition());
        }
        finally
        {
            workload.Cleanup();
        }
    }

    /// <summary>Context transitions repeat and buffer consume, expiry and interruption remain bounded.</summary>
    [Fact]
    public void ContextAndBufferWavesRepeat()
    {
        var workload = new ActionBenchmarks();
        workload.Setup();
        try
        {
            Assert.Equal(1.5f, workload.ContextAndAnalogWave());
            Assert.Equal(1.5f, workload.ContextAndAnalogWave());
            Assert.Equal(1, workload.BufferWave());
            Assert.Equal(1, workload.BufferWave());
            Assert.True(workload.RebindWave() > 0);
            Assert.True(workload.RebindWave() > 0);
            Assert.Equal(4, workload.ConstructRuntime());
            Assert.Equal(2, workload.BuildComposition().Bindings.Length);
        }
        finally
        {
            workload.Cleanup();
        }
    }

    /// <summary>Exclusive priority claims buttons while inherited analog bindings remain available.</summary>
    [Fact]
    public void ContextPriorityMapsInheritedStateAndEvents()
    {
        using var system = new ActionSystem(ActionBenchmarks.CreateProfile(RecognitionKind.Press));
        var events = new List<ActionEvent>();
        system.Changed += events.Add;
        system.QueueContextActivation("game");
        system.QueueContextActivation("overlay");
        InputRecord[] records =
        [
            new(new InputDeviceId(1), 1, TimeSpan.Zero, new KeyData(Key.A, true, false)),
            new(new InputDeviceId(1), 2, TimeSpan.Zero, new ControllerStickData(ControllerStick.Left, new System.Numerics.Vector2(0.25f, 0.5f))),
        ];
        system.Advance(records, TimeSpan.Zero);
        Assert.Equal(1, system.GetState("overlay", "a").Value.X);
        Assert.Equal(0, system.GetState("game", "a").Value.X);
        Assert.Equal(0.25f, system.GetState("game", "move").Value.X);
        Assert.Contains(events, item => item.ContextId == "overlay" && item.ActionId == "a" && item.Phase == ActionPhase.Started);
        Assert.DoesNotContain(events, item => item.ContextId == "game" && item.ActionId == "a" && item.Phase == ActionPhase.Started);
    }

    /// <summary>Building mutable definitions produces independent immutable snapshots.</summary>
    [Fact]
    public void CompositionSnapshotsMutableDefinitions()
    {
        Compose.Definitions.Action action = Compose.Action("jump", ActionValueKind.Button);
        Compose.Definitions.Profile node = Compose.Profile()[Compose.Profile.Actions()[action], Compose.Profile.Contexts()[Compose.Context(id: "game")]];
        ActionProfile original = node.Build();
        action.Sensitivity = 2;
        Assert.Equal(1, original.Actions[0].Sensitivity);
        Assert.Equal(2, node.Build().Actions[0].Sensitivity);
        action.Sensitivity = -1;
        Assert.Throws<ArgumentException>(() => node.Build());
    }

    /// <summary>Preparation and confirmation leave the immutable profile unchanged until Advance.</summary>
    [Fact]
    public void RebindIsPendingUntilBoundary()
    {
        using var system = new ActionSystem(ActionBenchmarks.CreateProfile(RecognitionKind.Press));
        ActionProfile original = system.ExportProfile();
        system.QueueContextActivation("game");
        system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
        RebindSession session = system.BeginRebind("a", new RebindOptions(TimeSpan.FromSeconds(1)));
        system.Advance(new InputRecord[] { new(new InputDeviceId(1), 1, TimeSpan.Zero, new KeyData(Key.J, true, false)) }, TimeSpan.Zero);
        Assert.Equal(InputControl.ForKey(Key.J), session.Candidate);
        ActionProfile candidate = session.PrepareProfile(RebindConflictPolicy.Allow);
        Assert.Equal(InputControl.ForKey(Key.J), candidate.Bindings[0].Control);
        Assert.Same(original, system.ExportProfile());
        session.Confirm(RebindConflictPolicy.Allow);
        Assert.Same(original, system.ExportProfile());
        system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.FromMilliseconds(1));
        Assert.Equal(InputControl.ForKey(Key.J), system.ExportProfile().Bindings[0].Control);
        Assert.True(session.IsComplete);
    }

    /// <summary>Settings JSON preserves inherited definitions and stable tokens and rejects invalid values.</summary>
    [Fact]
    public void SettingsRoundTripAndValidation()
    {
        InputActionSettings settings = SettingsBenchmarks.CreateSettings();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(settings, InputSettingsJsonContext.Default.InputActionSettings);
        InputActionSettings restored = JsonSerializer.Deserialize(json, InputSettingsJsonContext.Default.InputActionSettings)!;
        ActionProfile before = InputSettingsConverter.BuildProfile(settings);
        ActionProfile after = InputSettingsConverter.BuildProfile(restored);
        Assert.Equal(before.Actions.ToArray(), after.Actions.ToArray());
        Assert.Equal(before.Bindings.ToArray(), after.Bindings.ToArray());
        Assert.Equal("common", after.Contexts[1].ParentId);
        Assert.Equal(0.75f, after.Contexts[1].Actions[0].Sensitivity);
        Assert.Equal(before.Recognitions[0].Actions.ToArray(), after.Recognitions[0].Actions.ToArray());
        Assert.Equal(settings.BufferMaxEntries, restored.BufferMaxEntries);
        Assert.Equal(settings.BufferLifetimeSeconds, restored.BufferLifetimeSeconds);
        restored.Bindings[0].Control = "999";
        Assert.False(InputSettingsConverter.ValidateActions(restored));
        restored = SettingsBenchmarks.CreateSettings();
        restored.Contexts[0].ParentId = "game";
        Assert.False(InputSettingsConverter.ValidateActions(restored));
    }

    /// <summary>Real memory-backed commits advance revisions and coordinator application toggles controls.</summary>
    [Fact]
    public void CommittedRevisionAppliesRepeatedly()
    {
        var workload = new SettingsBenchmarks();
        workload.Setup();
        try
        {
            long initial = workload.Committed.Revision;
            Assert.Equal((int)Key.Space, workload.AppliedControl);
            Assert.Equal(initial, workload.PollCommitted());
            Assert.True(workload.CommitQueueApply() > 0);
            Assert.Equal((int)Key.Space, workload.PendingControl);
            Assert.Equal((int)Key.Space, workload.QueuedControl);
            Assert.Equal(initial + 1, workload.Committed.Revision);
            Assert.Equal("J", workload.Committed.Value.Bindings[0].Control);
            Assert.Equal((int)Key.J, workload.AppliedControl);
            Assert.True(workload.CommitQueueApply() > 0);
            Assert.Equal((int)Key.J, workload.PendingControl);
            Assert.Equal((int)Key.J, workload.QueuedControl);
            Assert.Equal(initial + 2, workload.Committed.Revision);
            Assert.Equal((int)Key.K, workload.AppliedControl);
            Assert.True(workload.Validate());
            Assert.False(workload.ValidateInvalid());
            Assert.Equal(2, workload.Deserialize().Bindings.Length);
            Assert.Equal(2, workload.ToSettings().Actions.Count);
            Assert.Equal((int)Key.Space, workload.ColdCoordinator());
        }
        finally
        {
            workload.Cleanup();
        }
    }
}
