using Lumyte.Composition.Sample;
using static Lumyte.Composition.Sample.Compose;

Compose.Definitions.Grid grid = Grid()[Text(with: [Grid.Column(1)])];
Console.WriteLine($"Children: {grid.Children.Count}, Column: {grid.Children[0].AttachedValues["Grid.Column"]}");

Compose.Definitions.ListView<int> list = ListView<int>(with: [ListViewFactory<int>().Select(2)])[1, 2, 3];
Console.WriteLine($"Items: {list.Items.Count}, Selected: {list.Selected}");

Compose.Definitions.Button button = Button()[Button.Background()[Image("sample.jpeg")]];
Console.WriteLine($"Background: {((Compose.Definitions.Image)button.BackgroundChildren[0]).Source}");
