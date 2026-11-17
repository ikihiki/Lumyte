using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI.Controls;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Exercises schema selection through typed HTTP requests and actual game-side input.</summary>
public sealed class LocatorVersionTests
{
    /// <summary>Every operation retains the highest ancestor role requirement.</summary>
    /// <param name="ancestorRole">The role in the locator chain.</param>
    /// <param name="operation">The operation minimum to combine with the role requirement.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData("listbox", "scroll")]
    [InlineData("listbox", "check")]
    [InlineData("listbox", "uncheck")]
    [InlineData("listbox", "set-value")]
    [InlineData("listbox", "set-range")]
    [InlineData("listitem", "scroll")]
    [InlineData("listitem", "check")]
    [InlineData("listitem", "uncheck")]
    [InlineData("listitem", "set-value")]
    [InlineData("listitem", "set-range")]
    [InlineData("combobox", "scroll")]
    [InlineData("combobox", "check")]
    [InlineData("combobox", "uncheck")]
    [InlineData("combobox", "set-value")]
    [InlineData("combobox", "set-range")]
    [InlineData("tabview", "check")]
    [InlineData("tabitem", "uncheck")]
    [InlineData("expander", "set-value")]
    [InlineData("menu", "check")]
    [InlineData("menuitem", "uncheck")]
    [InlineData("tooltip", "set-value")]
    [InlineData("menu", "set-range")]
    [InlineData("menu", "scroll")]
    [InlineData("plain", "scroll")]
    [InlineData("plain", "check")]
    [InlineData("plain", "uncheck")]
    [InlineData("plain", "set-value")]
    [InlineData("plain", "set-range")]
    public async Task TypedOperationsHonorAncestorSchemaAsync(string ancestorRole, string operation)
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(
            ListBox<string>(items: new[] { "a" }, keySelector: x => x, itemTemplate: _ => Stack()[CheckBox(testId: "check", height: 20), Slider(testId: "scalar", height: 20), RangeSlider(testId: "range", value: new SliderRange(0.2, 0.8), height: 20)], role: ancestorRole switch { "menu" => Lumyte.UI.UiRole.Menu, "menuitem" => Lumyte.UI.UiRole.MenuItem, "tooltip" => Lumyte.UI.UiRole.Tooltip, "combobox" => Lumyte.UI.UiRole.ComboBox, "tabview" => Lumyte.UI.UiRole.TabView, "tabitem" => Lumyte.UI.UiRole.TabItem, "expander" => Lumyte.UI.UiRole.Expander, _ => Lumyte.UI.UiRole.ListBox }, width: 200, height: 140, testId: "list")));
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        UiRemoteLocator root = ancestorRole == "plain" ? control.Page.GetByTestId("list") : control.Page.GetByRole(ancestorRole);
        switch (operation)
        {
            case "scroll":
                await root.ScrollAsync(new(0, 1));
                break;
            case "check":
                await root.GetByRole("checkbox").CheckAsync();
                break;
            case "uncheck":
                await root.GetByRole("checkbox").UncheckAsync();
                break;
            case "set-value":
                await root.GetByRole("slider").SetValueAsync(0.6);
                break;
            case "set-range":
                await root.GetByRole("rangeslider").SetRangeAsync(0.3, 0.7);
                break;
        }

        int[] versions = handler.ActionBodies.Select(body =>
        {
            using var envelope = JsonDocument.Parse(body);
            string plan = envelope.RootElement.GetProperty("arguments").GetProperty("plan-json").GetProperty("string").GetString()!;
            using var parsed = JsonDocument.Parse(plan);
            return parsed.RootElement.GetProperty("schemaVersion").GetInt32();
        }).ToArray();
        Assert.Equal(1, await root.CountAsync());
        await control.Page.Expect(root).ToHaveCountAsync(1);
        int minimum = operation == "scroll" ? 5 : operation is "check" or "uncheck" ? 4 : 6;
        Assert.Equal(ancestorRole == "plain" ? minimum : ancestorRole is "menu" or "menuitem" or "tooltip" ? 9 : ancestorRole is "tabview" or "tabitem" or "expander" ? 8 : 7, Assert.Single(versions));
    }

    /// <summary>Old supported catalogs admit only operation and role versions they advertise.</summary>
    /// <param name="version">The supported server catalog version.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task OlderCatalogRejectsNewRoleBeforeInputAsync(int version)
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(Stack()[CheckBox(testId: "check", height: 20), Slider(testId: "scalar", height: 20), ScrollView(height: 60, testId: "scroll")[SizedBox(width: 100, height: 200)]])) { SchemaVersion = version };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        await control.Page.GetByTestId("check").CheckAsync();
        if (version >= 5)
        {
            await control.Page.GetByTestId("scroll").ScrollAsync(new(0, 1));
        }

        if (version >= 6)
        {
            await control.Page.GetByTestId("scalar").SetValueAsync(0.5);
        }

        int submitted = handler.ActionBodies.Count;
        UiRemoteAutomationException failure = await Assert.ThrowsAsync<UiRemoteAutomationException>(() => control.Page.GetByRole("listbox").ScrollAsync(new(0, 1)));
        Assert.Equal("unsupported-catalog", failure.Code);
        Assert.Equal(submitted, handler.ActionBodies.Count);
    }
}
