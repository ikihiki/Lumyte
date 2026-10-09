using System.Security.Claims;
using Lumyte.Diagnostics.Server;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Verifies typed dashboard queries and operation input boundaries.</summary>
public sealed class DashboardModelTests
{
    /// <summary>Checks severity, trace and field filters together.</summary>
    [Fact]
    public void FiltersStructuredLogs()
    {
        DiagnosticEvent[] events = [Log("Info", "Information", "trace"), Log("Error", "Error", "trace"), Log("Other", "Error", "other")];
        DiagnosticEvent filtered = Assert.Single(DiagnosticTelemetryModel.Filter(events, "log", "log.level", "trace", "Warning"));
        Assert.Equal("Error", filtered.Name);
    }

    /// <summary>Checks hierarchy despite out-of-order arrivals and missing parents.</summary>
    [Fact]
    public void OrdersSpanParentsBeforeChildren()
    {
        DiagnosticEvent child = Span("child", "child", "parent");
        DiagnosticEvent parent = Span("parent", "parent", "0000000000000000");
        DiagnosticEvent orphan = Span("orphan", "orphan", "missing");
        DiagnosticTelemetryModel.TraceGroup trace = Assert.Single(DiagnosticTelemetryModel.Traces([child, orphan, parent]));
        Assert.True(trace.Spans.Single(item => item.Span == orphan).ParentMissing);
        Assert.True(Array.FindIndex(trace.Spans, item => item.Span == parent) < Array.FindIndex(trace.Spans, item => item.Span == child));
        Assert.Equal(1, trace.Spans.Single(item => item.Span == child).Depth);
    }

    /// <summary>Checks malformed cyclic span graphs remain bounded.</summary>
    [Fact]
    public void BoundsCyclicTraces()
    {
        DiagnosticTelemetryModel.TraceGroup trace = Assert.Single(DiagnosticTelemetryModel.Traces([Span("a", "a", "b"), Span("b", "b", "a")]));
        Assert.Equal(2, trace.Spans.Length);
        Assert.All(trace.Spans, item => Assert.InRange(item.Depth, 0, 32));
    }

    /// <summary>Checks canonical tags and distinct series.</summary>
    [Fact]
    public void SeparatesMetricTags()
    {
        var first = new DiagnosticEvent("metric", 1, "fps", DiagnosticValue.From(1L), null, null, null, 0, new Dictionary<string, DiagnosticValue> { ["b"] = DiagnosticValue.From("two"), ["a"] = DiagnosticValue.From("one") });
        DiagnosticEvent reordered = first with { Value = DiagnosticValue.From(2L), Fields = new Dictionary<string, DiagnosticValue> { ["a"] = DiagnosticValue.From("one"), ["b"] = DiagnosticValue.From("two") } };
        DiagnosticEvent other = first with { Fields = new Dictionary<string, DiagnosticValue> { ["a"] = DiagnosticValue.From("other") } };
        DiagnosticTelemetryModel.MetricSeries[] series = DiagnosticTelemetryModel.Metrics([first, reordered, other]);
        Assert.Equal(2, series.Length);
        Assert.Equal(2, series[0].Samples.Length);
        Assert.Equal(2L, series[0].Latest.Value.Int64);
    }

    /// <summary>Checks adjacent Int64 extrema retain their differences in graphs.</summary>
    [Fact]
    public void PreservesInt64Precision()
    {
        Assert.Equal("0,72 150,40 300,8", DiagnosticTelemetryModel.Points([DiagnosticValue.From(long.MaxValue - 2), DiagnosticValue.From(long.MaxValue - 1), DiagnosticValue.From(long.MaxValue)]));
        Assert.Equal("0,72 300,8", DiagnosticTelemetryModel.Points([DiagnosticValue.From(long.MinValue), DiagnosticValue.From(long.MaxValue)]));
        Assert.Equal("9223372036854775807", DiagnosticTelemetryModel.Text(DiagnosticValue.From(long.MaxValue)));
    }

    /// <summary>Checks floating-point ranges cannot overflow the normalization.</summary>
    [Fact]
    public void BoundsDoubleGraphsAndDurations()
    {
        Assert.Equal("0,72 300,8", DiagnosticTelemetryModel.Points([DiagnosticValue.From(-double.MaxValue), DiagnosticValue.From(double.MaxValue)]));
        Assert.Equal("150,40", DiagnosticTelemetryModel.Points([DiagnosticValue.From(1.0)]));
        Assert.Equal("5.0 ms", DiagnosticTelemetryModel.Duration(50000));
        Assert.Equal("0.0001 ms", DiagnosticTelemetryModel.Duration(1));
    }

    /// <summary>Checks type, finite values and schema constraints.</summary>
    [Fact]
    public void ValidatesOperationArguments()
    {
        Assert.Equal(long.MaxValue, DiagnosticOperationForm.Parse(new("id", DiagnosticValueKind.Int64), "9223372036854775807").Int64);
        Assert.Throws<ArgumentException>(() => DiagnosticOperationForm.Parse(new("id", DiagnosticValueKind.Int64), "9223372036854775808"));
        Assert.Throws<ArgumentException>(() => DiagnosticOperationForm.Parse(new("duration", DiagnosticValueKind.Int64, 0, 100), "101"));
        Assert.Throws<ArgumentException>(() => DiagnosticOperationForm.Parse(new("ratio", DiagnosticValueKind.Double), "NaN"));
        Assert.Throws<ArgumentException>(() => DiagnosticOperationForm.Parse(new("text", DiagnosticValueKind.String, MaxLength: 3), "long"));
        Assert.Throws<ArgumentException>(() => DiagnosticOperationForm.Parse(new("flag", DiagnosticValueKind.Boolean), "yes"));
        var form = new DiagnosticOperationForm(new("edit", "Edit", DiagnosticPermission.Edit, [], [], true)) { Revision = "9223372036854775807" };
        Assert.Equal(long.MaxValue, form.Build("sample").ExpectedRevision);
        Assert.NotEqual(form.Build("sample").RequestId, form.Build("sample").RequestId);
    }

    /// <summary>Checks ticket expiry and the finite browser-login budget.</summary>
    [Fact]
    public void ExpiresAndBoundsAuthenticationTickets()
    {
        var clock = new MutableClock();
        using var tickets = new DiagnosticBrowserTickets(clock);
        ClaimsPrincipal first = tickets.Create();
        CancellationToken lifetime = tickets.Require(first);
        for (int index = 1; index < 128; index++)
        {
            tickets.Create();
        }

        Assert.Throws<InvalidOperationException>(() => tickets.Create());
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Throws<UnauthorizedAccessException>(() => tickets.Require(first));
        ClaimsPrincipal replacement = tickets.Create();
        Assert.True(lifetime.IsCancellationRequested);
        tickets.Require(replacement);
    }

    private static DiagnosticEvent Log(string name, string level, string trace) => new("log", 1, name, DiagnosticValue.From(name), trace, "span", null, 0, new Dictionary<string, DiagnosticValue> { ["log.level"] = DiagnosticValue.From(level) });

    private static DiagnosticEvent Span(string name, string id, string parent) => new("span", 1, name, DiagnosticValue.From("Unset"), "trace", id, parent, 50000, new Dictionary<string, DiagnosticValue>());

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
