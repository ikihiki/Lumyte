using System.Text.Json;
using Lumyte.Diagnostics;
using Lumyte.Diagnostics.UI.Automation;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Application.Navigation.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Checks real diagnostic jobs and metadata across one capacity-preserving publication.</summary>
public sealed class DiagnosticsTests
{
    /// <summary>Checks Click completes before commit, target replacement fits a full budget and old job results remain readable.</summary>
    /// <param name="withMetadata">Whether optional navigation observation is registered.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiagnosticClickCanNavigateWithoutWaitingOnItsOwnLeaseAsync(bool withMetadata)
    {
        var provider = new PageProvider();
        UiNavigationRequest? navigation = null;
        UiPageKey<UiNoArgs> home = provider.Register("/", (activation, _) => new(Button(width: 120, height: 40, testId: "go", onClick: () => navigation = activation.Context.Navigation.RequestNavigate("/other"))));
        provider.Register("/other", (_, _) => new(Panel()));
        UiAutomationDiagnosticsRuntime? runtime = null;
        UiNavigationDiagnostics? contributor = null;
        await using var harness = new NavigationHarness(
            provider,
            nav => nav.RequestNavigate(home, default),
            diagnostics: true,
            configure: builder =>
            {
                Capture<UiAutomationDiagnosticsRuntime>(builder.Services, value => runtime = value);
                if (withMetadata)
                {
                    Capture<UiNavigationDiagnostics>(builder.Services, value => contributor = value);
                }
            },
            navigationDiagnostics: withMetadata);
        await harness.ReadyAsync();
        var automation = new DiagnosticOperationSet(new UiAutomationDiagnostics(runtime!));
        var session = Guid.NewGuid();
        UiNavigationWindowSnapshot before = await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot);
        Guid target = before.Current!.TargetId!.Value;
        DiagnosticOperationResult acquired = await CallAsync(harness, automation, session, "control-acquire", Args(("target-id", target.ToString("D")), ("duration-ms", 5000L)));
        string control = Success(acquired)["control-id"].String!;
        string plan = UiRemoteSchema.SerializePlan(new(1, "click", [new("test-id", Value: "go")]));
        DiagnosticOperationResult started = await CallAsync(harness, automation, session, "action-start", Args(("target-id", target.ToString("D")), ("control-id", control), ("sequence", 1L), ("plan-json", plan), ("job-timeout-ms", 5000L)));
        string job = Success(started)["job-id"].String!;
        string? jobState = null;
        await harness.WaitAsync(() =>
        {
            DiagnosticOperationResult result = Call(automation, session, "action-get", Args(("job-id", job)));
            jobState = Success(result)["state"].String;
            return jobState is "Succeeded" or "Failed" or "Canceled" or "TimedOut";
        });
        Assert.Equal("Succeeded", jobState);
        await harness.WaitAsync(() => navigation?.Completion.IsCompleted == true);
        Assert.Equal(UiNavigationStatus.Committed, (await navigation!.Completion).Status);
        await harness.IdleAsync();
        UiNavigationWindowSnapshot after = await harness.App.Dispatcher.InvokeAsync(harness.Context.View.CaptureNavigationSnapshot);
        Assert.NotEqual(target, after.Current!.TargetId);
        Assert.Equal("Succeeded", Success(await CallAsync(harness, automation, session, "action-get", Args(("job-id", job))))["state"].String);
        DiagnosticOperationResult old = await CallAsync(harness, automation, session, "observe-start", Args(("target-id", target.ToString("D")), ("plan-json", UiRemoteSchema.SerializePlan(new(1, "snapshot", []))), ("job-timeout-ms", 1000L)));
        Assert.NotEqual("success", old.Status);
        if (!withMetadata)
        {
            Assert.Null(contributor);
            return;
        }

        var state = new DiagnosticOperationSet(contributor!);
        IReadOnlyDictionary<string, DiagnosticValue> receipt = Success(await CallAsync(harness, state, session, "state", Args(("window-context-id", string.Empty))));
        Assert.Equal(after, Assert.Single(UiNavigationStateSchema.Parse(receipt["json"].String!)));
        using var document = JsonDocument.Parse(receipt["json"].String!);
        JsonElement json = document.RootElement;
        Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
        JsonElement window = Assert.Single(json.GetProperty("windows").EnumerateArray());
        Assert.Equal(after.WindowContextId, window.GetProperty("windowContextId").GetGuid());
        JsonElement current = window.GetProperty("current");
        Assert.Equal(after.Current.TargetId, current.GetProperty("targetId").GetGuid());
        Assert.Equal(after.Current.EntryId, current.GetProperty("entryId").GetGuid());
        Assert.Equal(["activationId", "entryId", "navigatorId", "route", "surfaceId", "targetId"], current.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("invalid-argument", (await CallAsync(harness, state, session, "state", Args(("window-context-id", "invalid")))).Code);
        Assert.Equal("not-found", (await CallAsync(harness, state, session, "state", Args(("window-context-id", Guid.NewGuid().ToString("D"))))).Code);
    }

    /// <summary>Rejects incompatible observation formats before clients reuse their target identifiers.</summary>
    /// <param name="json">An incompatible state document.</param>
    [Theory]
    [InlineData("{\"schemaVersion\":2,\"windows\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"windows\":[],\"extra\":true}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"windows\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"windows\":{}}")]
    [InlineData("{\"schemaVersion\":\"1\",\"windows\":[]}")]
    public void InvalidObservationSchemasAreRejected(string json) => Assert.Throws<JsonException>(() => UiNavigationStateSchema.Parse(json));

    private static void Capture<T>(IServiceCollection services, Action<T> capture)
        where T : class
    {
        ServiceDescriptor descriptor = services.Single(item => item.ServiceType == typeof(T));
        services.Remove(descriptor);
        services.AddScoped(provider =>
        {
            var instance = (T)descriptor.ImplementationFactory!(provider);
            capture(instance);
            return instance;
        });
    }

    private static Task<DiagnosticOperationResult> CallAsync(NavigationHarness harness, DiagnosticOperationSet operations, Guid session, string operation, Dictionary<string, DiagnosticValue> arguments) => harness.App.Dispatcher.InvokeAsync(() => Call(operations, session, operation, arguments)).AsTask();

    private static DiagnosticOperationResult Call(DiagnosticOperationSet operations, Guid session, string operation, Dictionary<string, DiagnosticValue> arguments) => operations.Invoke(operation, arguments, new(Guid.NewGuid(), session, new(0, 0), "operator", null, CancellationToken.None), new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe, DiagnosticPermission.OverrideInput });

    private static Dictionary<string, DiagnosticValue> Args(params (string Name, object Value)[] arguments) => arguments.ToDictionary(pair => pair.Name, pair => pair.Value is string text ? DiagnosticValue.From(text) : DiagnosticValue.From((long)pair.Value));

    private static IReadOnlyDictionary<string, DiagnosticValue> Success(DiagnosticOperationResult result)
    {
        Assert.True(result.Status == "success", result.Code + ": " + result.Message);
        return result.Values!;
    }
}
