using Lumyte.Composition;
using Lumyte.Composition.Sample;
using static Lumyte.Composition.Sample.Compose;

Compose.Definitions.Grid grid = Grid()[Text(with: [Grid.Column(1)])];
Console.WriteLine($"Children: {grid.Children.Count}, Column: {grid.Children[0].AttachedValues["Grid.Column"]}");

Compose.Definitions.ListView<int> list = ListView<int>(with: [ListViewFactory<int>().Select(2)])[1, 2, 3];
Console.WriteLine($"Items: {list.Items.Count}, Selected: {list.Selected}");

Compose.Definitions.Button button = Button()[Button.Background()[Image("sample.jpeg")]];
Console.WriteLine($"Background: {((Compose.Definitions.Image)button.BackgroundChildren[0]).Source}");

var greeting = (Compose.Definitions.FunctionWidget)Greeting(key: "greeting");
Console.WriteLine($"Function: {((Compose.Definitions.Text)greeting.Evaluate()).Content}, Key: {greeting.Key}");
var typed = (Compose.Definitions.FunctionWidget)Typed(7);
if (((Compose.Definitions.Text)typed.Evaluate()).Content != "7")
{
    throw new InvalidOperationException("The generic function host did not evaluate the generated declaration.");
}

int templateCalls = 0;
CompositionSlotAssignment<Compose.Definitions.ListView<int>> itemTemplate = ListViewFactory<int>().Item()[
    (value, owner) => Text(content: $"{++templateCalls}:{value}/{owner.Items.Count}")];
Compose.Definitions.ListView<int> templatedList = ListView<int>()[4, 5, itemTemplate];
if (templateCalls != 0 || ((Compose.Definitions.Text)templatedList.ItemTemplate!(4, templatedList)).Content != "1:4/2" || templateCalls != 1)
{
    throw new InvalidOperationException("Template assignment must defer typed evaluation to the host.");
}

Compose.Definitions.Text fixedThumb = Text(content: "fixed");
Compose.Definitions.Button templatedButton = Button()[Button.Thumb()[fixedThumb]];
if (!ReferenceEquals(fixedThumb, templatedButton.ThumbTemplate!(0)) || !ReferenceEquals(fixedThumb, templatedButton.ThumbTemplate!(1)))
{
    throw new InvalidOperationException("Fixed templates must preserve the original composition identity.");
}

CompositionSlotAssignment<Compose.Definitions.ListView<int>> reusable = ListViewFactory<int>().Item()[
    (value, owner) => Text(content: $"{value}/{owner.Items.Count}")];
Compose.Definitions.ListView<int> otherList = ListView<int>()[1, 2, 3, reusable];
if (((Compose.Definitions.Text)otherList.ItemTemplate!(2, otherList)).Content != "2/3")
{
    throw new InvalidOperationException("Reused templates must receive the evaluating list explicitly.");
}

Console.WriteLine("Templates: typed item/list, fixed thumb, reused assignment passed");
