using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

using Lumyte.Input;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class ActionBindingDocumentTests
{
    [Fact]
    public void DocumentExposesCompositePartsAsIndependentSlots()
    {
        var move = new InputAction<Vector2>("game.move");
        ActionMap map = ActionMap("Gameplay")[
            new Vector2CompositeBinding(
                move,
                InputControls.Key(Key.W),
                InputControls.Key(Key.S),
                InputControls.Key(Key.A),
                InputControls.Key(Key.D)),
            new ActionBinding<Vector2>(move, InputControls.GamepadLeftStick())
        ];

        ActionBindingDocument document = ActionBindingDocument.Create([map]);

        Assert.Collection(
            document.Slots,
            slot => Assert.Equal(ActionBindingPart.Up, slot.Part),
            slot => Assert.Equal(ActionBindingPart.Down, slot.Part),
            slot => Assert.Equal(ActionBindingPart.Left, slot.Part),
            slot => Assert.Equal(ActionBindingPart.Right, slot.Part),
            slot =>
            {
                Assert.Equal(ActionBindingPart.Primary, slot.Part);
                Assert.Equal(InputValueKind.Vector2, slot.ValueKind);
            });
    }

    [Fact]
    public void CandidateReportsConflictAndConfirmChangesTheDocument()
    {
        var jump = new InputAction<bool>("game.jump");
        var interact = new InputAction<bool>("game.interact");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space)),
            new ActionBinding<bool>(interact, InputControls.Key(Key.E))
        ];
        ActionBindingDocument document = ActionBindingDocument.Create([map]);
        ActionBindingSlot jumpSlot = Assert.Single(
            document.Slots,
            slot => slot.ActionId == jump.Id);
        RebindingSession session = document.BeginRebinding(jumpSlot.Id);

        bool accepted = session.TryOffer(
            RebindingCandidate.From(InputControls.Key(Key.E)));
        ActionBindingConflict conflict = Assert.Single(session.Conflicts);
        session.Confirm();

        Assert.True(accepted);
        Assert.Equal(interact.Id, conflict.ConflictingSlot.ActionId);
        Assert.Equal(InputControlDescriptor.From(InputControls.Key(Key.E)), jumpSlot.Control);
        Assert.Equal(RebindingSessionStatus.Confirmed, session.Status);
    }

    [Fact]
    public void CancelLeavesTheDocumentUnchanged()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        ActionBindingDocument document = ActionBindingDocument.Create([map]);
        ActionBindingSlot slot = Assert.Single(document.Slots);
        RebindingSession session = document.BeginRebinding(slot.Id);

        session.TryOffer(RebindingCandidate.From(InputControls.Key(Key.J)));
        session.Cancel();

        Assert.Equal(slot.DefaultControl, slot.Control);
        Assert.Equal(RebindingSessionStatus.Canceled, session.Status);
    }

    [Fact]
    public void IncompatibleCandidateIsNotAccepted()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        ActionBindingDocument document = ActionBindingDocument.Create([map]);
        RebindingSession session = document.BeginRebinding(Assert.Single(document.Slots).Id);

        bool accepted = session.TryOffer(
            RebindingCandidate.From(InputControls.GamepadLeftStick()));

        Assert.False(accepted);
        Assert.Null(session.Candidate);
        Assert.Equal(RebindingSessionStatus.Waiting, session.Status);
    }

    [Fact]
    public void OverrideJsonRoundTripsWithoutChangingDefaults()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        ActionBindingDocument source = ActionBindingDocument.Create([map]);
        ActionBindingSlot sourceSlot = Assert.Single(source.Slots);
        RebindingSession session = source.BeginRebinding(sourceSlot.Id);
        session.TryOffer(RebindingCandidate.From(InputControls.Key(Key.J)));
        session.Confirm();

        string json = source.SaveOverrides();
        ActionBindingDocument restored = ActionBindingDocument.Create([map]);
        restored.LoadOverrides(json);
        ActionBindingSlot restoredSlot = Assert.Single(restored.Slots);

        Assert.Equal(InputControlDescriptor.From(InputControls.Key(Key.J)), restoredSlot.Control);
        Assert.Equal(InputControlDescriptor.From(InputControls.Key(Key.Space)), restoredSlot.DefaultControl);
        Assert.Equal(InputControls.Key(Key.Space), ((ActionBinding<bool>)map.Bindings[0]).TypedControl);
    }

    [Fact]
    public void ExplicitBindingIdsSurviveDefinitionReordering()
    {
        var jump = new InputAction<bool>("game.jump");
        var interact = new InputAction<bool>("game.interact");
        ActionMap original = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
            {
                BindingId = "jump-keyboard",
            },
            new ActionBinding<bool>(interact, InputControls.Key(Key.E))
            {
                BindingId = "interact-keyboard",
            }
        ];
        ActionBindingDocument source = ActionBindingDocument.Create([original]);
        ActionBindingSlot jumpSlot = Assert.Single(
            source.Slots,
            slot => slot.ActionId == jump.Id);
        RebindingSession session = source.BeginRebinding(jumpSlot.Id);
        session.TryOffer(RebindingCandidate.From(InputControls.Key(Key.J)));
        session.Confirm();
        string json = source.SaveOverrides();
        ActionMap reordered = ActionMap("Gameplay")[
            new ActionBinding<bool>(interact, InputControls.Key(Key.E))
            {
                BindingId = "interact-keyboard",
            },
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
            {
                BindingId = "jump-keyboard",
            }
        ];

        ActionBindingDocument restored = ActionBindingDocument.Create([reordered]);
        restored.LoadOverrides(json);

        ActionBindingSlot restoredJump = Assert.Single(
            restored.Slots,
            slot => slot.ActionId == jump.Id);
        Assert.Equal(InputControlDescriptor.From(InputControls.Key(Key.J)), restoredJump.Control);
    }

    [Fact]
    public void UnknownBindingOverridesAreIgnoredAndDiagnosed()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
            {
                BindingId = "jump-keyboard",
            }
        ];
        ActionBindingDocument document = ActionBindingDocument.Create([map]);
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == InteractionDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        const string json =
            """
            [
              {
                "slotId": "Gameplay/removed-binding:Primary",
                "control": {
                  "device": "Keyboard",
                  "name": "J"
                }
              }
            ]
            """;

        document.LoadOverrides(json);

        ActionBindingSlot slot = Assert.Single(document.Slots);
        Assert.Equal(slot.DefaultControl, slot.Control);
        Activity activity = Assert.Single(
            stopped,
            candidate => Equals(
                candidate.GetTagItem("interaction.binding.ignored_count"),
                1));
        ActivityEvent ignored = Assert.Single(activity.Events);
        var actual = new
        {
            activity.OperationName,
            AppliedCount = activity.GetTagItem("interaction.binding.applied_count"),
            IgnoredCount = activity.GetTagItem("interaction.binding.ignored_count"),
            EventName = ignored.Name,
            SlotId = ignored.Tags.Single(tag =>
                tag.Key == "interaction.binding.slot_id").Value,
            Reason = ignored.Tags.Single(tag =>
                tag.Key == "interaction.binding.ignore_reason").Value,
        };
        var expected = new
        {
            OperationName = "ActionBindingDocument.LoadOverrides",
            AppliedCount = (object?)0,
            IgnoredCount = (object?)1,
            EventName = "BindingOverride.Ignored",
            SlotId = (object?)"Gameplay/removed-binding:Primary",
            Reason = (object?)"slot_not_found",
        };
        Assert.Equal(expected, actual);
    }
}
