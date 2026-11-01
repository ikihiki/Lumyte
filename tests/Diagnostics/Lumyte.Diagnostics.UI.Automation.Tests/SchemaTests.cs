using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Xunit;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Checks the closed UI JSON envelope independently of UI, native code and transport serialization.</summary>
public sealed class SchemaTests
{
    /// <summary>Checks unknown/duplicate/type-confused fields cannot become input operations.</summary>
    /// <param name="json">Malformed or unsupported JSON.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"kind\":\"snapshot\",\"locator\":[],\"actorId\":\"admin\"}")]
    [InlineData("{\"schemaVersion\":1,\"kind\":\"snapshot\",\"kind\":\"click\",\"locator\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"kind\":\"snapshot\",\"locator\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"kind\":\"snapshot\",\"locator\":[],\"position\":{\"x\":0,\"y\":0}}")]
    [InlineData("{\"schemaVersion\":1,\"kind\":\"click\",\"locator\":[{\"kind\":\"test-id\",\"value\":\"save\"}],\"position\":{\"x\":-1,\"y\":0}}")]
    [InlineData("{\"schemaVersion\":1,\"kind\":\"press\",\"locator\":[{\"kind\":\"test-id\",\"value\":\"save\"}],\"key\":\"enter\"}")]
    [InlineData("{\"schemaVersion\":1,\"kind\":\"press\",\"locator\":[{\"kind\":\"test-id\",\"value\":\"save\"}],\"key\":\"Enter\",\"modifiers\":[\"Shift\",\"Shift\"]}")]
    [InlineData("{\"schemaVersion\":1,\"kind\":\"expect\",\"locator\":[{\"kind\":\"role\",\"role\":\"button\"}],\"predicate\":\"visible\",\"count\":0}")]
    [InlineData("{\"schemaVersion\":1,\"kind\":\"click\",\"locator\":[{\"kind\":\"test-id\",\"value\":\"save\",\"value\":\"other\"}]}")]
    public void RejectsInvalidPlans(string json) => Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan(json));

    /// <summary>Checks canonical modifier normalization, explicit nullable fields and copied arrays.</summary>
    [Fact]
    public void CanonicalPlansAreDetachedAndStable()
    {
        string[] modifiers = ["Shift", "Control"];
        var original = new UiRemotePlan(1, "press", [new("role", Role: "button", Name: "保存")], Key: "Enter", Modifiers: modifiers);
        string canonical = UiRemoteSchema.SerializePlan(original);
        modifiers[0] = "Alt";
        UiRemotePlan parsed = UiRemoteSchema.ParsePlan(canonical);
        Assert.Equal(["Control", "Shift"], parsed.Modifiers!);
        Assert.Equal(canonical, UiRemoteSchema.SerializePlan(parsed));
        Assert.True(UiRemoteSchema.IsAction(parsed));
    }

    /// <summary>Checks large payloads, query depth and nonfinite or unrepresentable coordinates are rejected.</summary>
    [Fact]
    public void RejectsUnboundedPlans()
    {
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan(new string(' ', 32769)));
        var deep = new UiRemotePlan(1, "click", Enumerable.Repeat(new UiRemoteLocatorStep("test-id", "save"), 9).ToArray());
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(deep));
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(Harness.Plan("click") with { Position = new(double.MaxValue, 0) }));
        Assert.Throws<ArgumentException>(() => UiRemoteSchema.SerializePlan(Harness.Plan("click") with { Position = new(double.NaN, 0) }));
    }
}
