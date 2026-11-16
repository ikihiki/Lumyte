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
    [InlineData("{\"schemaVersion\":5,\"kind\":\"snapshot\",\"locator\":[]}")]
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

    /// <summary>Checks versioned text fields and candidate identities stay closed and bounded.</summary>
    [Fact]
    public void TextAndCandidatePlansRequireTheirSchemaAndExplicitFields()
    {
        var fill = new UiRemotePlan(2, "fill", [new("test-id", Value: "editor")], Text: "かな😀");
        Assert.True(UiRemoteSchema.IsAction(UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(fill))));
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(fill with { SchemaVersion = 1 }));
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(fill with { Text = null }));
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(fill with { Kind = "click" }));
        var candidate = new UiRemotePlan(3, "ime-select", [], Candidate: new(Guid.NewGuid(), "1", "2", "3"), CandidateIndex: 0);
        Assert.True(UiRemoteSchema.IsAction(UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(candidate))));
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(candidate with { SchemaVersion = 2 }));
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(candidate with { CandidateIndex = -1 }));
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(candidate with { Candidate = candidate.Candidate! with { Revision = "03" } }));
    }

    /// <summary>Checks ordinary input authorization does not expose text or permit document mutation.</summary>
    [Fact]
    public void TextPermissionsDefaultToDenied()
    {
        using var h = new Harness();
        Guid control = h.Acquire();
        Assert.Equal("text-input-disabled", h.Start(new(3, "fill", [new("test-id", Value: "editor")], Text: "private"), control).Code);
        Assert.Equal("text-inspection-disabled", h.Start(new(3, "expect-value", [new("test-id", Value: "editor")], Text: "private")).Code);
        Assert.Equal("text-inspection-disabled", h.Start(new(3, "ime-candidates", [])).Code);
    }

    /// <summary>Checks v4 desired-state operations retain strict version and field boundaries.</summary>
    [Fact]
    public void SelectionPlansAreClosedVersionFourActions()
    {
        foreach (string kind in new[] { "check", "uncheck", "select-option" })
        {
            var plan = new UiRemotePlan(4, kind, [new("test-id", Value: "choice")], OptionKey: kind == "select-option" ? "日本語" : null);
            Assert.True(UiRemoteSchema.IsAction(UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(plan))));
            foreach (int version in new[] { 1, 2, 3 })
            {
                Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(plan with { SchemaVersion = version }));
            }

            Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(plan with { Position = new(0, 0) }));
            Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(plan with { Text = "secret" }));
            Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(plan with { Key = "Enter" }));
        }

        foreach (string role in new[] { "togglebutton", "checkbox", "switch", "radiogroup", "radiobutton" })
        {
            var query = new UiRemotePlan(4, "count", [new("role", Role: role)]);
            Assert.False(UiRemoteSchema.IsAction(UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(query))));
            Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(query with { SchemaVersion = 3 }));
        }

        var select = new UiRemotePlan(4, "select-option", [new("test-id", Value: "group")]);
        foreach (string? key in new[] { null, string.Empty, " ", new string('a', 257) })
        {
            Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(select with { OptionKey = key }));
        }

        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(select with { Kind = "check", OptionKey = "a" }));
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan("{\"schemaVersion\":3,\"kind\":\"snapshot\",\"locator\":[],\"optionKey\":null}"));
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan("{\"schemaVersion\":4,\"kind\":\"select-option\",\"locator\":[{\"kind\":\"test-id\",\"value\":\"group\"}],\"optionKey\":\"a\",\"optionKey\":\"b\"}"));
    }

    /// <summary>Checks desired-state actions cannot be submitted through observation authority.</summary>
    [Fact]
    public void SelectionCannotEscalateObservation()
    {
        using var h = new Harness();
        foreach (string kind in new[] { "check", "uncheck", "select-option" })
        {
            var plan = new UiRemotePlan(4, kind, [new("test-id", Value: "save")], OptionKey: kind == "select-option" ? "a" : null);
            Assert.NotEqual("success", h.Start(plan).Status);
        }
    }
}
