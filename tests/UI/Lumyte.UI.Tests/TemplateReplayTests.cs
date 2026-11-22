using Lumyte.Composition;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using Probe = Lumyte.UI.Tests.TemplateReplayKit.Definitions.Probe;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks typed Slot recipes survive argument-only reconstruction without evaluating templates.</summary>
public sealed class TemplateReplayTests
{
    /// <summary>Checks replay preserves the template and supplies the reconstructed owner explicitly.</summary>
    [Fact]
    public void ArgumentReconstructionPreservesUnevaluatedTemplate()
    {
        var width = Signal.Create<float?>(20);
        int evaluations = 0;
        CompositionTemplate<Probe, Widget> template = owner =>
        {
            evaluations++;
            return Rectangle(width: owner.Width);
        };
        Probe original = TemplateReplayKit.Probe(width: width)[TemplateReplayKit.Probe.Body()[template]];
        using var surface = new UiSurface(original);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        for (int index = 1; index <= 3; index++)
        {
            width.Value = 20 + index;
            surface.UpdateLayout();
            Probe current = Assert.IsType<Probe>(surface.Root);
            Assert.NotSame(original, current);
            Assert.Same(template, current.Template);
            Assert.Equal(1, current.AssignmentCount);
            Assert.Equal(20 + index, current.Width);
            Assert.Equal(0, evaluations);
        }

        Probe final = Assert.IsType<Probe>(surface.Root);
        Assert.Equal(23, Assert.IsType<Definitions.Rectangle>(final.Template!(final)).Width);
        Assert.Equal(1, evaluations);
        Assert.Equal(20, original.Width);
    }

    /// <summary>Checks factory-time with assignments replay once and later Slot assignments still win.</summary>
    [Fact]
    public void WithAndOutsideSlotsDoNotAccumulateReplayActions()
    {
        var width = Signal.Create<float?>(20);
        CompositionTemplate<Probe, Widget> first = _ => Rectangle(width: 1);
        CompositionTemplate<Probe, Widget> last = _ => Rectangle(width: 2);
        CompositionSlotAssignment<Probe> withSlot = TemplateReplayKit.Probe.Body()[first];
        Probe original = TemplateReplayKit.Probe(width: width, with: [target => withSlot.Apply(target)])[TemplateReplayKit.Probe.Body()[last]];
        using var surface = new UiSurface(original);
        for (int index = 1; index <= 3; index++)
        {
            width.Value = 20 + index;
            surface.UpdateTree();
            Probe current = Assert.IsType<Probe>(surface.Root);
            Assert.Same(last, current.Template);
            Assert.Equal(2, current.AssignmentCount);
        }
    }

    /// <summary>Checks a specified-empty Slot retains its non-child intent through repeated argument reconstruction.</summary>
    [Fact]
    public void StaticSlotIntentSurvivesArgumentReconstruction()
    {
        var width = Signal.Create<float?>(20);
        Probe original = TemplateReplayKit.Probe(width: width);
        original.SpecifyEmptySlot();
        using var surface = new UiSurface(original);
        for (int index = 1; index <= 3; index++)
        {
            width.Value = 20 + index;
            surface.UpdateTree();
            Probe current = Assert.IsType<Probe>(surface.Root);
            Assert.NotSame(original, current);
            Assert.True(current.HasSpecifiedSlot);
            Assert.Equal(20 + index, current.Width);
        }
    }

    /// <summary>Checks a captured declaration-tree recipe replays template settings on fresh declarations.</summary>
    [Fact]
    public void CapturedRecipeRetainsTemplateWithoutRetainingItsOwner()
    {
        CompositionTemplate<Probe, Widget> template = owner => Rectangle(width: owner.Width);
        Probe original = TemplateReplayKit.Probe(width: 17)[TemplateReplayKit.Probe.Body()[template]];
        UiDeclarationRecipe recipe = Assert.IsType<UiDeclarationRecipe>(UiDeclarationRecipe.Capture(original));
        Probe first = Assert.IsType<Probe>(recipe.Build());
        Probe second = Assert.IsType<Probe>(recipe.Build());
        Assert.NotSame(original, first);
        Assert.NotSame(first, second);
        Assert.Same(template, first.Template);
        Assert.Same(template, second.Template);
        Assert.Equal(1, first.AssignmentCount);
        Assert.Equal(1, second.AssignmentCount);
    }
}
