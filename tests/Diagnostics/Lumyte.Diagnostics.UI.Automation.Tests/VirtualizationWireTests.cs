using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI.Collections;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Exercises owner-scoped keys through typed HTTP and the ordinary server input lease.</summary>
public sealed class VirtualizationWireTests
{
    /// <summary>Observations do not realize offscreen items; authorized input reveals and clicks them.</summary>
    /// <returns>The asynchronous check.</returns>
    [Trait("Execution", "Integration")]
    [Fact]
    public async Task KeyObservationAndInputHaveSeparateAuthorityAsync()
    {
        int templates = 0;
        int changes = 0;
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(Panel()[ListBox<int>(
            items: Enumerable.Range(0, 1000).ToArray(),
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ =>
            {
                Interlocked.Increment(ref templates);
                return SizedBox(height: 20);
            },
            onSelectionChanged: _ => Interlocked.Increment(ref changes),
            testId: "list",
            width: 100,
            height: 60,
            virtualization: new UiItemVirtualization(20, 0))]));
        handler.SchemaVersion = 10;
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        UiRemoteLocator observed = client.Page(target.Id).GetByTestId("list").GetByItemKey("999");
        Assert.Equal(0, await observed.CountAsync());
        int before = Volatile.Read(ref templates);
        await Assert.ThrowsAsync<InvalidOperationException>(() => observed.BringIntoViewAsync());
        Assert.Equal(before, Volatile.Read(ref templates));
        Assert.Empty(handler.ActionBodies);
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        UiRemoteLocator item = control.Page.GetByTestId("list").GetByItemKey("999");
        await item.BringIntoViewAsync();
        Assert.Equal(1, await item.CountAsync());
        await item.ClickAsync();
        Assert.Equal(1, Volatile.Read(ref changes));
        Assert.InRange(Volatile.Read(ref templates), 3, 6);
        foreach (string body in handler.ActionBodies)
        {
            using var request = JsonDocument.Parse(body);
            string plan = request.RootElement.GetProperty("arguments").GetProperty("plan-json").GetProperty("string").GetString()!;
            Assert.Equal(10, UiRemoteSchema.ParsePlan(plan).SchemaVersion);
        }
    }

    /// <summary>Frozen schemas cannot interpret item keys as test IDs or new input commands.</summary>
    [Fact]
    public void ItemQueriesRequireTenAndClosedFields()
    {
        for (int version = 1; version <= 9; version++)
        {
            var plan = new UiRemotePlan(version, "count", [new("test-id", Value: "list"), new("item-key", Value: "999")]);
            Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(plan)));
        }

        var valid = new UiRemotePlan(10, "bring-item", [new("test-id", Value: "list"), new("item-key", Value: "999")]);
        Assert.True(UiRemoteSchema.IsAction(UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(valid))));
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(valid with { Locator = [new("item-key", Value: "999")] })));
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(valid with { Locator = [new("test-id", Value: "list"), new("item-key", Value: new string('a', 257))] })));
    }
}
