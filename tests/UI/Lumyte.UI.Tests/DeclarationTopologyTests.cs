using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks stable keys, computed arrays and instance-local topology.</summary>
public sealed class DeclarationTopologyTests
{
    /// <summary>Checks generated key parameters use strings instead of reactive argument adapters.</summary>
    [Fact]
    public void KeyIsAnInitializationOnlyLiteralParameter()
    {
        System.Reflection.ParameterInfo parameter = Assert.Single(Panel.Method.GetParameters(), item => item.Name == "key");
        Assert.Equal(typeof(Lumyte.Composition.Optional<string>), parameter.ParameterType);
        Assert.Contains(typeof(System.Runtime.CompilerServices.IsExternalInit), typeof(Definitions.Widget).GetProperty("Key")!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());
    }

    /// <summary>Checks a declaration shared by separate parent branches is validated in each receiver's topology.</summary>
    [Fact]
    public void SharedDeclarationsHaveIndependentValidationContexts()
    {
        var observed = new List<Widget?>();
        var child = new ParentObserver(observed.Add);
        Definitions.Panel first = Panel()[child];
        Definitions.Panel second = Panel()[child];
        using var surface = new UiSurface(Panel()[first, second]);
        Assert.Equal(new Widget[] { first, second }, observed);
        Assert.NotSame(surface.RootElement.Children[0].Children[0], surface.RootElement.Children[1].Children[0]);
    }

    /// <summary>Checks shrinking a custom component's slots removes stale public topology.</summary>
    /// <param name="before">The original slot count.</param>
    /// <param name="after">The replacement slot count.</param>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void ShrinkingSlotsClearsUnmountedChildren(int before, int after)
    {
        using var surface = new UiSurface(new VariableSlots(before, true));
        UiElement parent = surface.RootElement;
        UiElement child = Assert.Single(parent.Children);
        UiElementChildren[] previousSlots = parent.Slots.ToArray();
        surface.Rebuild(new VariableSlots(after, false));
        surface.UpdateTree();
        Assert.Same(parent, surface.RootElement);
        Assert.Empty(parent.Children);
        Assert.False(child.IsMounted);
        Assert.Null(child.Parent);
        Assert.All(previousSlots.Skip(after), slot => Assert.Empty(slot));
        if (after > 0)
        {
            Assert.Same(previousSlots[0], parent.Slots[0]);
        }
    }

    private sealed class ParentObserver(Action<Widget?> observe) : Definitions.Panel
    {
        protected override void ValidateDeclaration(UiWidgetValidationContext context) => observe(context.GetParent(this));
    }

    private sealed class VariableSlots : Definitions.Widget
    {
        public VariableSlots(int count, bool withContent)
        {
            for (int index = 0; index < count; index++)
            {
                WidgetChildren slot = CreateChildren();
                if (withContent && index == count - 1)
                {
                    slot.Initialize(Panel());
                }
            }
        }
    }
}
