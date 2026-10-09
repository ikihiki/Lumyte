using System.Globalization;
using System.Numerics;

namespace Lumyte.Diagnostics.Server;

internal static class DiagnosticTelemetryModel
{
    internal static readonly string[] LogLevels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    public static string Text(DiagnosticValue value) => value.Kind switch
    {
        DiagnosticValueKind.Boolean => value.Boolean ? "true" : "false",
        DiagnosticValueKind.Int64 => value.Int64.ToString(CultureInfo.InvariantCulture),
        DiagnosticValueKind.Double => value.Double.ToString("R", CultureInfo.InvariantCulture),
        DiagnosticValueKind.String => value.String ?? string.Empty,
        _ => "?",
    };

    public static DiagnosticEvent[] Filter(DiagnosticEvent[] events, string? kind, string query, string traceId, string level)
    {
        int threshold = Array.IndexOf(LogLevels, level);
        return events.Where(item => (kind == null || item.Kind == kind) && (traceId.Length == 0 || item.TraceId == traceId)
            && (kind != "log" || threshold < 0 || Array.IndexOf(LogLevels, Level(item)) >= threshold)
            && (query.Length == 0 || string.Join(' ', item.Name, item.Kind, Text(item.Value), item.TraceId, item.SpanId, string.Join(' ', item.Fields.Select(field => field.Key + "=" + Text(field.Value)))).Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    public static string Level(DiagnosticEvent item) => item.Fields.TryGetValue("log.level", out DiagnosticValue level) ? Text(level) : "Unknown";

    public static string Duration(long ticks) => ((decimal)ticks / TimeSpan.TicksPerMillisecond).ToString("0.0###", CultureInfo.InvariantCulture) + " ms";

    public static TraceGroup[] Traces(DiagnosticEvent[] events)
    {
        return events.Where(item => item.Kind == "span" && item.TraceId != null).GroupBy(item => item.TraceId!)
            .Select(group =>
            {
                DiagnosticEvent[] spans = group.ToArray();
                var byId = spans.Where(span => span.SpanId != null).GroupBy(span => span.SpanId!).ToDictionary(items => items.Key, items => items.First(), StringComparer.Ordinal);
                var visited = new HashSet<DiagnosticEvent>();
                var rows = new List<SpanRow>();
                void Visit(DiagnosticEvent span, int depth)
                {
                    if (!visited.Add(span))
                    {
                        return;
                    }

                    bool missing = span.ParentSpanId is not null and not "0000000000000000" && !byId.ContainsKey(span.ParentSpanId);
                    rows.Add(new(span, Math.Min(depth, 32), missing));
                    foreach (DiagnosticEvent child in spans.Where(item => span.SpanId != null && item.ParentSpanId == span.SpanId))
                    {
                        Visit(child, depth + 1);
                    }
                }

                foreach (DiagnosticEvent root in spans.Where(span => span.ParentSpanId == null || !byId.ContainsKey(span.ParentSpanId)))
                {
                    Visit(root, 0);
                }

                foreach (DiagnosticEvent span in spans)
                {
                    Visit(span, 0);
                }

                return new TraceGroup(group.Key, rows[0].Span.Name, spans.Max(span => span.Timestamp), spans.Max(span => span.DurationTicks), spans.Any(span => Text(span.Value) == "Error"), rows.ToArray());
            }).OrderByDescending(group => group.Latest).ToArray();
    }

    public static MetricSeries[] Metrics(DiagnosticEvent[] events)
    {
        return events.Where(item => item.Kind == "metric").GroupBy(item => (item.Name, item.Value.Kind, Tags: string.Join("\n", item.Fields.OrderBy(field => field.Key, StringComparer.Ordinal).Select(field => $"{field.Key.Length}:{field.Key}:{field.Value.Kind}:{Text(field.Value).Length}:{Text(field.Value)}"))))
            .Select(group => new MetricSeries(group.Key.Name, group.Last(), group.Select(item => item.Value).ToArray())).ToArray();
    }

    public static string Points(DiagnosticValue[] samples)
    {
        double[] normalized;
        if (samples.Length == 0)
        {
            return string.Empty;
        }

        if (samples.All(value => value.Kind == DiagnosticValueKind.Int64))
        {
            BigInteger[] values = samples.Select(value => new BigInteger(value.Int64)).ToArray();
            BigInteger minimum = values.Min();
            BigInteger range = values.Max() - minimum;
            normalized = values.Select(value => range.IsZero ? 0.5 : (double)((value - minimum) * 10000 / range) / 10000).ToArray();
        }
        else if (samples.All(value => value.Kind == DiagnosticValueKind.Double && double.IsFinite(value.Double)))
        {
            double scale = Math.Max(1, samples.Max(value => Math.Abs(value.Double)));
            double[] values = samples.Select(value => value.Double / scale).ToArray();
            double minimum = values.Min();
            double range = values.Max() - minimum;
            normalized = values.Select(value => range == 0 ? 0.5 : (value - minimum) / range).ToArray();
        }
        else
        {
            return string.Empty;
        }

        return string.Join(' ', normalized.Select((value, index) => string.Create(CultureInfo.InvariantCulture, $"{(normalized.Length == 1 ? 150 : index * 300.0 / (normalized.Length - 1)):0.####},{72 - (value * 64):0.####}")));
    }

    internal sealed record SpanRow(DiagnosticEvent Span, int Depth, bool ParentMissing);

    internal sealed record TraceGroup(string TraceId, string Name, long Latest, long Longest, bool HasError, SpanRow[] Spans);

    internal sealed record MetricSeries(string Name, DiagnosticEvent Latest, DiagnosticValue[] Samples);
}
