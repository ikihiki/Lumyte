using Lumyte.Settings;

namespace Lumyte.Diagnostics.Settings;

internal sealed class SettingsDiagnostics<T>(IEditableOptions<T> settings, SettingsDiagnosticFields<T>.Field[] fields, IGameExecutionIdentity identity) : IDiagnosticContributor, IAsyncDisposable
    where T : class, new()
{
    private readonly IEditableOptions<T> _settings = settings;
    private readonly SettingsDiagnosticFields<T>.Field[] _fields = fields;
    private readonly CancellationTokenSource _lifetime = new();
    private Job? _job;

    public void Configure(DiagnosticBuilder builder)
    {
        builder.Operation(new("read", "Read settings", DiagnosticPermission.Observe, [], [.. _fields.Select(field => field.Schema), new("load-status", DiagnosticValueKind.String)]), (_, _) =>
        {
            using IDisposable correlation = SettingsTelemetry.BeginScope(identity.InstanceId);
            SettingsSnapshot<T> snapshot = _settings.Current;
            return DiagnosticOperationResult.Success(new SnapshotValues(snapshot.Value, _settings.LoadResult.Status.ToString(), _fields), snapshot.Revision);
        });
        SettingsDiagnosticFields<T>.Field[] editable = _fields.Where(field => field.Editable).ToArray();
        if (editable.Length == 0)
        {
            return;
        }

        builder.Operation(new("save", "Start saving settings", DiagnosticPermission.Edit, editable.Select(field => field.Schema).ToArray(), [new("job-id", DiagnosticValueKind.String)], RequiresRevision: true), (context, arguments) =>
        {
            using IDisposable correlation = SettingsTelemetry.BeginScope(identity.InstanceId);
            if (_job is { Completion.IsCompleted: false })
            {
                return DiagnosticOperationResult.Reject("busy", "A settings save is still running.");
            }

            SettingsEdit<T> edit = _settings.BeginEdit();
            if (edit.BaseRevision != context.ExpectedRevision)
            {
                return DiagnosticOperationResult.Conflict(edit.BaseRevision);
            }

            foreach (SettingsDiagnosticFields<T>.Field field in editable)
            {
                field.Apply(edit.Value, arguments);
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, context.CancellationToken);
            _job?.Cancellation.Dispose();
            _job = new(context.RequestId.ToString("D"), context.SessionId, cancellation, SaveAsync(edit, cancellation.Token));
            return DiagnosticOperationResult.Success(new ReceiptValues(_job.Id), edit.BaseRevision);
        });
        builder.Operation(new("save-result", "Check settings save result", DiagnosticPermission.Observe, [new("job-id", DiagnosticValueKind.String)], [new("write-status", DiagnosticValueKind.String)]), (context, arguments) =>
        {
            if (_job == null || _job.Session != context.SessionId || _job.Id != arguments.GetString("job-id"))
            {
                return DiagnosticOperationResult.Reject("not-found", "Settings save result is not retained for this session.");
            }

            // Result is accessed only after completion; file I/O never blocks the pump.
            Outcome outcome = _job.Completion.IsCompletedSuccessfully ? _job.Completion.Result : new("Pending", _settings.Revision);
            return DiagnosticOperationResult.Success(new StatusValues(outcome.Status), outcome.Revision);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_job != null)
        {
            await _job.Completion.ConfigureAwait(false);
            _job.Cancellation.Dispose();
        }

        _lifetime.Dispose();
    }

    private async Task<Outcome> SaveAsync(SettingsEdit<T> edit, CancellationToken cancellationToken)
    {
        using IDisposable correlation = SettingsTelemetry.BeginScope(identity.InstanceId);
        try
        {
            SettingsSaveResult<T> result = await _settings.SaveAsync(edit, cancellationToken).ConfigureAwait(false);
            return new(result.Status.ToString(), result.Snapshot.Revision);
        }
        catch (OperationCanceledException)
        {
            return new("Cancelled", _settings.Revision);
        }
        catch (Exception)
        {
            return new("Failed", _settings.Revision);
        }
    }

    private sealed record Outcome(string Status, long Revision);

    private sealed record Job(string Id, Guid Session, CancellationTokenSource Cancellation, Task<Outcome> Completion);

    private sealed class SnapshotValues(T value, string status, SettingsDiagnosticFields<T>.Field[] fields) : DiagnosticOutputValues
    {
        public override int Count => fields.Length + 1;

        public override void WriteTo<TWriter>(ref TWriter writer)
        {
            foreach (SettingsDiagnosticFields<T>.Field field in fields)
            {
                field.Write(value, ref writer);
            }

            writer.Write("load-status", status);
        }
    }

    private sealed class ReceiptValues(string id) : DiagnosticOutputValues
    {
        public override int Count => 1;

        public override void WriteTo<TWriter>(ref TWriter writer) => writer.Write("job-id", id);
    }

    private sealed class StatusValues(string status) : DiagnosticOutputValues
    {
        public override int Count => 1;

        public override void WriteTo<TWriter>(ref TWriter writer) => writer.Write("write-status", status);
    }
}
