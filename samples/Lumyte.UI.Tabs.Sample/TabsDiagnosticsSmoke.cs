using System.Diagnostics;
using System.Text.Json;
using Lumyte.Diagnostics;
using Lumyte.Diagnostics.UI.Automation;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI.Controls;
using Lumyte.UI.SampleSupport;
using static Lumyte.UI.Controls.Compose;

namespace Lumyte.UI.Tabs.Sample;

internal static class TabsDiagnosticsSmoke
{
    internal static void Run()
    {
        using var surface = new UiSurface(Expander(testId: "details")[Expander.Content()[Slider(value: 0.25, testId: "numeric")]]);
        surface.SetViewport(new(240, 160));
        surface.UpdateLayout();
        var host = new UiSampleAutomationHost(surface, _ =>
        {
            surface.UpdateLayout();
            return ValueTask.CompletedTask;
        });
        using var runtime = new UiAutomationDiagnosticsRuntime(TimeProvider.System, new());
        using UiAutomationDiagnosticRegistration registration = runtime.Register("tabs", surface, host, new() { AllowInput = true });
        var operations = new DiagnosticOperationSet(new UiAutomationDiagnostics(runtime));
        var session = Guid.NewGuid();
        long frame = 0;
        Dictionary<string, DiagnosticValue> target = new() { ["target-id"] = DiagnosticValue.From(registration.TargetId.ToString("D")), ["duration-ms"] = DiagnosticValue.From(5000L) };
        var control = Guid.Parse(Call("control-acquire", target)["control-id"].String!);
        var expand = new UiRemotePlan(8, "expand", [new("test-id", Value: "details")]);
        Complete(expand, control, 1);
        Complete(expand, control, 2);
        if (!((ExpanderElement)surface.RootElement).IsExpanded)
        {
            throw new InvalidOperationException("Native diagnostic expansion did not use ordinary input.");
        }

        for (int version = 1; version <= 8; version++)
        {
            IReadOnlyDictionary<string, DiagnosticValue> state = Complete(new(version, "snapshot", []));
            Dictionary<string, DiagnosticValue> artifact = new()
            {
                ["job-id"] = state["job-id"],
                ["artifact-id"] = state["result-artifact-id"],
                ["offset"] = DiagnosticValue.From(0L),
                ["count"] = DiagnosticValue.From(32768L),
            };
            byte[] bytes = Convert.FromBase64String(Call("artifact-read", artifact)["base64"].String!);
            using var json = JsonDocument.Parse(bytes);
            foreach (JsonElement node in json.RootElement.GetProperty("nodes").EnumerateArray())
            {
                if (node.EnumerateObject().Count() != (version < 4 ? 18 : version < 6 ? 23 : version < 8 ? 24 : 25))
                {
                    throw new InvalidOperationException("Native diagnostic legacy shape changed.");
                }
            }

            if (version == 8)
            {
                UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
                if (snapshot.Nodes.Single(node => node.TestId == "details").IsExpanded != true || snapshot.Nodes.Single(node => node.TestId == "numeric").Numeric?.Value != 0.25)
                {
                    throw new InvalidOperationException("Native schema-eight generated metadata failed.");
                }
            }
        }

        runtime.BeginStop();
        while (!runtime.IsStopped)
        {
            runtime.Advance(new(frame++, Stopwatch.GetTimestamp()));
        }

        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return;

        IReadOnlyDictionary<string, DiagnosticValue> Call(string operation, Dictionary<string, DiagnosticValue> arguments)
        {
            var context = new DiagnosticOperationContext(Guid.NewGuid(), session, new(frame, 0), "native-smoke", null, CancellationToken.None);
            DiagnosticOperationResult result = operations.Invoke(operation, arguments, context, new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe, DiagnosticPermission.OverrideInput });
            return result.Status == "success" ? result.Values! : throw new InvalidOperationException(result.Code);
        }

        IReadOnlyDictionary<string, DiagnosticValue> Complete(UiRemotePlan plan, Guid? lease = null, long sequence = 0)
        {
            Dictionary<string, DiagnosticValue> arguments = new()
            {
                ["target-id"] = DiagnosticValue.From(registration.TargetId.ToString("D")),
                ["plan-json"] = DiagnosticValue.From(UiRemoteSchema.SerializePlan(plan)),
                ["job-timeout-ms"] = DiagnosticValue.From(1000L),
            };
            if (lease is { } id)
            {
                arguments["control-id"] = DiagnosticValue.From(id.ToString("D"));
                arguments["sequence"] = DiagnosticValue.From(sequence);
            }

            IReadOnlyDictionary<string, DiagnosticValue> accepted = Call(lease is null ? "observe-start" : "action-start", arguments);
            Dictionary<string, DiagnosticValue> query = new() { ["job-id"] = accepted["job-id"] };
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                runtime.Advance(new(frame++, Stopwatch.GetTimestamp()));
                surface.UpdateLayout();
                IReadOnlyDictionary<string, DiagnosticValue> state = Call(lease is null ? "observe-get" : "action-get", query);
                if (state["state"].String == "Succeeded")
                {
                    return state;
                }

                if (state["state"].String is "Failed" or "Canceled" or "TimedOut")
                {
                    throw new InvalidOperationException(state["code"].String);
                }

                Thread.Sleep(1);
            }

            throw new InvalidOperationException("Native diagnostic job did not finish.");
        }
    }
}
