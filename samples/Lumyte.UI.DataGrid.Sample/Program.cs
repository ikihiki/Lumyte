using System.Text.Json;
using Lumyte.Composition;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Input.Abstractions;
using Lumyte.Mathematics.Colors;
using Lumyte.Signals;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.Collections;
using Lumyte.UI.DataGrid.Sample;
using Lumyte.UI.Media.Parley;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Media.Compose;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.DataGrid.Sample <explicit-font.ttf>");
    return 2;
}

var wire = new UiRemotePlan(12, "count", [new("role", Role: "datagrid"), new("item-key", Value: "999"), new("column-key", Value: "name")]);
byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(wire, UiAutomationJsonContext.Default.UiRemotePlan);
if (UiRemoteSchema.ParsePlan(System.Text.Encoding.UTF8.GetString(encoded)).SchemaVersion != 12)
{
    throw new InvalidOperationException("The source-generated schema-twelve metadata failed.");
}

using var engine = new ParleyTextEngine2D();
string family;
using (FileStream font = File.OpenRead(args[0]))
{
    family = engine.AddFont(font);
}

var provider = new ParleyTextLayoutProvider(engine, new() { FontFamily = family });
var suffix = Signal.Create("initial");
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
int cells = 0;
Person[] people = Enumerable.Range(0, 1000).Select(index => new Person(index, "Person " + index)).ToArray();
DataGridColumn<Person>[] columns = [new("id", "ID", 100, comparer: Comparer<Person>.Create((left, right) => left.Id.CompareTo(right.Id))), new("name", "Name", 260)];
UiApplicationBuilder builder = UiApplication.CreateBuilder([]);
builder.UseHeadless(new() { ClientSize = new(400, 240) });
builder.UseDiagnostics(new() { Target = new() { AllowInput = true } });
builder.UseWindows(_ => [Window(
    "main",
    "DataGrid",
    root: value =>
{
    context = value;
    Lumyte.UI.Compose.Definitions.Widget Cell(DataGridCellContext<Person> cell, Lumyte.UI.Collections.Compose.Definitions.DataGrid<Person> owner)
    {
        cells++;
        if (!ReferenceEquals(owner.Items, people))
        {
            throw new InvalidOperationException("The cell received a foreign declaration owner.");
        }

        return Lumyte.UI.Compose.Panel()[
            Lumyte.UI.Compose.Rectangle(fill: new SRgba<float>(0.08f, 0.12f, 0.18f, 1)),
            Text(value: (cell.Column.Key == "id" ? cell.Row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : cell.Row.Name) + " " + suffix.Value, layoutProvider: provider)];
    }

    CompositionSlotAssignment<Lumyte.UI.Collections.Compose.Definitions.DataGrid<Person>> headerSlot = DataGridFactory<Person>().Header()[
        (column, owner) => Text(value: column.Name, layoutProvider: provider)];
    CompositionSlotAssignment<Lumyte.UI.Collections.Compose.Definitions.DataGrid<Person>> cellSlot = DataGridFactory<Person>().Cell()[
        (cell, owner) => Cell(cell, owner)];
    return DataGrid<Person>(items: people, keySelector: person => person.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), columns: columns, testId: "grid", height: 240, virtualization: new UiItemVirtualization(24, 2))[
        headerSlot, cellSlot];
},
    initialClientSize: new(400, 240),
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task running = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
var page = new UiAutomationPage(context!.Automation);
await page.GetByRole(UiRole.Button, "ID").ClickAsync();
await page.GetByRole(UiRole.Button, "ID").PressAsync(Key.ArrowRight);
UiLocator cellTarget = page.GetByTestId("grid").GetByItemKey("999").GetByColumnKey("name");
await cellTarget.BringIntoViewAsync();
await cellTarget.ClickAsync();
await cellTarget.PressAsync(Key.ArrowLeft);
await context.Automation.RunOnUiThreadAsync(surface =>
{
    var grid = (DataGridElement<Person>)surface.RootElement;
    suffix.Value = "updated";
    surface.UpdateLayout();
    UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
    var canvas = new Canvas2D();
    canvas.Begin();
    surface.Record(canvas);
    DrawList2D drawing = canvas.End();
    UiElement[] recorded = Descendants(surface.RootElement).ToArray();
    bool hasText = recorded.OfType<Lumyte.UI.Media.TextElement>().Any(text => text.CurrentLayout is { } layout && layout.Drawings.Count > 0 && text.Size.X > 0 && text.Size.Y > 0);
    bool hasFill = recorded.OfType<RectangleElement>().Any(rectangle => rectangle.Widget.Fill.A > 0 && rectangle.Size.X > 0 && rectangle.Size.Y > 0);
    bool updatedCell = recorded.OfType<Lumyte.UI.Media.TextElement>().Any(text => text.Widget.Value == "Person 999 updated");
    if (drawing.Count < 2 || !hasText || !hasFill || !updatedCell)
    {
        throw new InvalidOperationException("The explicit font text and cell fill were not recorded through the normal surface.");
    }

    if (grid.SelectedKey != "999" || grid.Sort.Direction != DataGridSortDirection.Ascending || grid.ColumnWidths[0] != 108
        || !snapshot.Nodes.Any(node => node.GridCell is { RowKey: "999", ColumnKey: "name" })
        || !snapshot.Nodes.Any(node => node.IsFocused && node.GridCell is { RowKey: "999", ColumnKey: "id" }) || cells > 200)
    {
        throw new InvalidOperationException("Grid selection, typed content refresh, sort, resize or bounded realization failed.");
    }

    return true;
});
await app.StopAsync();
await running;
Console.WriteLine("Typed DataGrid header sort, keyboard resize, keyed cell reveal and content refresh passed.");
return 0;

static IEnumerable<UiElement> Descendants(UiElement root)
{
    yield return root;
    foreach (UiElement child in root.Children)
    {
        foreach (UiElement descendant in Descendants(child))
        {
            yield return descendant;
        }
    }
}
