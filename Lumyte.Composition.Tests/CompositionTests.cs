using Lumyte.Composition;
using static Lumyte.Composition.Tests.TestKit;

[assembly: CompositionDefaults("TestKit")]

namespace Lumyte.Composition.Tests;

public abstract class TestNode
{
    [ComposeParameter]
    protected string? Name { get; set; }

    public string? CurrentName => Name;
}

public abstract class TestContainer : TestNode
{
    [ComposeParameter]
    protected bool Enabled { get; set; } = true;

    [ComposeContent]
    protected IReadOnlyList<TestNode> Children { get; set; } = [];

    public bool IsEnabled => Enabled;

    public IReadOnlyList<TestNode> CurrentChildren => Children;
}

[Composable]
public partial class Group : TestContainer
{
    [ComposeParameter]
    private float Spacing { get; set; }

    public float CurrentSpacing => Spacing;
}

[Composable(Name = "Leaf")]
public partial class Item : TestNode
{
    [ComposeParameter]
    private string Text { get; set; } = string.Empty;

    public string CurrentText => Text;
}

public sealed class CompositionTests
{
    [Fact]
    public void FactoryAppliesNamedParameters()
    {
        Group group = Group(spacing: 8, name: "root", enabled: false);

        Assert.Equal("root", group.CurrentName);
        Assert.False(group.IsEnabled);
        Assert.Equal(8, group.CurrentSpacing);
    }

    [Fact]
    public void OmittedParametersKeepDeclaredDefaults()
    {
        Group group = Group();

        Assert.Null(group.CurrentName);
        Assert.True(group.IsEnabled);
    }

    [Fact]
    public void IndexerBuildsNestedStructure()
    {
        Group root = Group(name: "root")[
            Leaf(text: "first"),
            Group(name: "nested")[
                Leaf(text: "second")
            ]
        ];

        Assert.Equal(2, root.CurrentChildren.Count);
        Group nested = Assert.IsType<Group>(root.CurrentChildren[1]);
        Assert.Single(nested.CurrentChildren);
        Assert.Equal("second", Assert.IsType<Item>(nested.CurrentChildren[0]).CurrentText);
    }

    [Fact]
    public void PositionalParametersRunFromConcreteTypeToBaseTypes()
    {
        Group group = Group(8, false, "root");

        Assert.Equal(8, group.CurrentSpacing);
        Assert.Equal("root", group.CurrentName);
        Assert.False(group.IsEnabled);
    }
}
