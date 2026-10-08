using Lumyte.Composition.Sample;
using static Lumyte.Composition.Sample.Compose;

Compose.Definitions.Grid grid = Grid()[Text(with: [Grid.Column(1)])];
Console.WriteLine($"Children: {grid.Children.Count}, Column: {grid.Children[0].AttachedValues["Grid.Column"]}");
