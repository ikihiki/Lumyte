using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.Input.Abstractions;
using Lumyte.UI;
using Lumyte.UI.Automation;

namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Owns bounded UI jobs and detached results on one diagnostic execution owner.</summary>
public sealed partial class UiAutomationDiagnosticsRuntime
{
    private static UiRemoteSnapshot Snapshot(UiAutomationSnapshot snapshot)
    {
        if (snapshot.Nodes.Count > 10000)
        {
            throw new InvalidOperationException("result-too-large");
        }

        UiRemoteNode[] nodes = snapshot.Nodes.Select(MapNode).ToArray();
        return new(snapshot.SurfaceId, snapshot.Sequence.ToString(CultureInfo.InvariantCulture), nodes);
    }

    private static UiRemoteNode MapNode(UiAutomationNode node) => new(
        node.Id.Value.ToString(CultureInfo.InvariantCulture),
        node.ParentId?.Value.ToString(CultureInfo.InvariantCulture),
        node.TestId,
        node.Name,
        node.Role.ToString().ToLowerInvariant(),
        new(node.Bounds.X, node.Bounds.Y, node.Bounds.Width, node.Bounds.Height),
        new(node.ClippedBounds.X, node.ClippedBounds.Y, node.ClippedBounds.Width, node.ClippedBounds.Height),
        node.IsVisible,
        node.IsEnabled,
        node.HitTestVisible,
        node.IsFocusable,
        node.IsFocused,
        node.IsPressed,
        node.HasActivePress,
        new[] { UiAutomationActions.Click, UiAutomationActions.Focus }.Where(action => (node.Actions & action) != 0).Select(action => action.ToString().ToLowerInvariant()).ToArray());

    private static UiLocator Locate(UiAutomationPage page, UiRemoteLocatorStep[] steps)
    {
        UiLocator? locator = null;
        foreach (UiRemoteLocatorStep step in steps)
        {
            if (step.Kind == "test-id")
            {
                locator = locator is null ? page.GetByTestId(step.Value!) : locator.GetByTestId(step.Value!);
            }
            else
            {
                UiRole role = step.Role == "button" ? UiRole.Button : UiRole.Group;
                locator = locator is null ? page.GetByRole(role, step.Name, step.IncludeHidden) : locator.GetByRole(role, step.Name, step.IncludeHidden);
            }
        }

        return locator!;
    }

    private static async Task AssertAsync(UiAutomationPage page, UiLocator locator, UiRemotePlan plan, CancellationToken token)
    {
        UiExpect expect = page.Expect(locator);
        Task task = plan.Predicate switch
        {
            "visible" => expect.ToBeVisibleAsync(cancellationToken: token),
            "hidden" => expect.ToBeHiddenAsync(cancellationToken: token),
            "enabled" => expect.ToBeEnabledAsync(cancellationToken: token),
            "focused" => expect.ToBeFocusedAsync(cancellationToken: token),
            _ => expect.ToHaveCountAsync(plan.Count!.Value, cancellationToken: token),
        };
        await task;
    }

    private async Task<Outcome> ExecuteAsync(Job job)
    {
        try
        {
            IUiAutomationHost host = job.Host is IUiAutomationScreenshotHost screenshot ? new TrackedScreenshotHost(job.Host, job, screenshot) : new TrackedHost(job.Host!, job);
            var page = new UiAutomationPage(host, new() { Timeout = Remaining(job.Started, job.Timeout) });
            UiRemotePlan plan = job.Plan!;
            UiLocator? locator = plan.Locator.Length == 0 ? null : Locate(page, plan.Locator);
            CancellationToken token = job.Cancellation.Token;
            byte[]? result = null;
            string contentType = "application/json";
            switch (plan.Kind)
            {
                case "click":
                    UiClickOptions? click = plan.Position is { } point ? new() { Position = new Vector2((float)point.X, (float)point.Y) } : null;
                    await locator!.ClickAsync(click, token);
                    break;
                case "focus":
                    await locator!.FocusAsync(cancellationToken: token);
                    break;
                case "press":
                    UiModifiers modifiers = UiModifiers.None;
                    foreach (string modifier in plan.Modifiers!)
                    {
                        modifiers |= Enum.Parse<UiModifiers>(modifier, ignoreCase: false);
                    }

                    await locator!.PressAsync(Enum.Parse<Key>(plan.Key!, ignoreCase: false), modifiers, cancellationToken: token);
                    break;
                case "count":
                    result = JsonSerializer.SerializeToUtf8Bytes(await locator!.CountAsync(token), UiAutomationJsonContext.Default.Int32);
                    break;
                case "expect":
                    await AssertAsync(page, locator!, plan, token);
                    break;
                case "snapshot":
                    result = SerializeBounded(Snapshot(await page.SnapshotAsync(token)), UiAutomationJsonContext.Default.UiRemoteSnapshot);
                    break;
                case "screenshot":
                    result = await page.ScreenshotAsync(token);
                    contentType = "image/png";
                    break;
            }

            return new(result, contentType, [], string.Empty);
        }
        catch (UiAutomationException failure)
        {
            string code = failure.Kind switch
            {
                UiAutomationFailureKind.Timeout => "timeout",
                UiAutomationFailureKind.AmbiguousMatch => "ambiguous-match",
                UiAutomationFailureKind.UnsupportedAction => "unsupported-action",
                UiAutomationFailureKind.TargetChanged => "target-changed",
                UiAutomationFailureKind.HostUnavailable => "host-unavailable",
                _ => "action-failed",
            };
            return new(null, string.Empty, failure.Trace.ToArray(), code);
        }
        catch (OperationCanceledException failure)
        {
            return new(null, string.Empty, failure.Data["Lumyte.UI.Automation.Trace"] as string[] ?? [], "canceled");
        }
        catch (NotSupportedException)
        {
            return new(null, string.Empty, [], "unsupported-action");
        }
        catch (Exception failure)
        {
            return new(null, string.Empty, [], failure.Message == "result-too-large" ? "result-too-large" : "action-failed");
        }
    }

    private byte[] SerializeBounded<T>(T value, JsonTypeInfo<T> type)
    {
        // Utf8JsonWriter requests its working span before encoding a large field.
        var buffer = new BoundedJsonBuffer(checked((int)_options.MaximumArtifactBytes));
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(writer, value, type);
        writer.Flush();
        return buffer.ToArray();
    }

    private void Finish(Job job, Outcome outcome)
    {
        bool canceled = job.State == "CancelRequested";
        job.State = canceled ? job.Code == "job-timeout" ? "TimedOut" : "Canceled" : outcome.Code == string.Empty ? "Succeeded" : outcome.Code == "timeout" ? "TimedOut" : "Failed";
        if (!canceled)
        {
            job.Code = outcome.Code;
        }

        byte[]? trace = null;
        bool traceTooLarge = false;
        try
        {
            trace = outcome.Trace.Length == 0 ? null : SerializeBounded(outcome.Trace.TakeLast(64).ToArray(), UiAutomationJsonContext.Default.StringArray);
        }
        catch (InvalidOperationException failure) when (failure.Message == "result-too-large")
        {
            traceTooLarge = true;
        }

        byte[]? result = canceled ? null : outcome.Result;
        long bytes = (result?.LongLength ?? 0) + (trace?.LongLength ?? 0);
        if (traceTooLarge || (result?.LongLength ?? 0) > _options.MaximumArtifactBytes || (trace?.LongLength ?? 0) > _options.MaximumArtifactBytes || bytes > _options.MaximumStoredArtifactBytes - _artifactBytes)
        {
            job.State = "Failed";
            job.Code = "result-too-large";
        }
        else
        {
            job.ResultId = result is null ? null : Store(job, result, outcome.ContentType);
            job.TraceId = trace is null ? null : Store(job, trace, "application/json");
        }

        job.Completed = _clock.GetTimestamp();
        job.Revision++;
        job.Task = null;
        job.Plan = null;
        job.Host = null;
        job.RegisteredSurface = null;
        job.Cancellation.Dispose();
        Audit("complete", job);
    }

    private Guid Store(Job job, byte[] bytes, string contentType)
    {
        var id = Guid.NewGuid();
        byte[] fixedBytes = (byte[])bytes.Clone();
        _artifacts.Add(id, new(id, job.Id, fixedBytes, contentType, Convert.ToHexString(SHA256.HashData(fixedBytes))));
        _artifactBytes += bytes.LongLength;
        return id;
    }

    private void Forget(Job job)
    {
        foreach (Guid id in new[] { job.ResultId, job.TraceId }.OfType<Guid>())
        {
            if (_artifacts.Remove(id, out Artifact? artifact))
            {
                _artifactBytes -= artifact.Bytes.LongLength;
            }
        }

        _jobs.Remove(job.Id);
        job.Cancellation.Dispose();
    }

    private class TrackedHost(IUiAutomationHost host, Job job) : IUiAutomationHost
    {
        public TimeProvider TimeProvider => host.TimeProvider;

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => host.RunOnUiThreadAsync(
            surface =>
        {
            if (!ReferenceEquals(surface, job.RegisteredSurface))
            {
                throw new ObjectDisposedException(nameof(IUiAutomationHost), "The registered surface was replaced.");
            }

            return action(surface);
        },
            cancellationToken);

        public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => host.NextFrameAsync(cancellationToken);

        public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default)
        {
            IUiAutomationInputLease lease = await host.AcquireInputAsync(cancellationToken);
            job.MarkInputSent();
            return lease;
        }
    }

    private sealed class TrackedScreenshotHost(IUiAutomationHost host, Job job, IUiAutomationScreenshotHost screenshot) : TrackedHost(host, job), IUiAutomationScreenshotHost
    {
        public ValueTask<byte[]> CapturePngAsync(CancellationToken cancellationToken = default) => screenshot.CapturePngAsync(cancellationToken);
    }
}
