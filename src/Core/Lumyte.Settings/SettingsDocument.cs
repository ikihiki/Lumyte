using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumyte.Settings;

internal sealed class SettingsDocument : ISettingsDocument, IDisposable
{
    private readonly PersistedSettingsSource _source;
    private readonly SettingsTelemetryCollector _telemetry;
    private readonly IServiceProvider _services;
    private readonly PersistedOptionsExtensions.Registration[] _registrations;
    private JsonObject _document;
    private bool _protected;

    public SettingsDocument(PersistedSettingsSource source, IServiceProvider services, IEnumerable<PersistedOptionsExtensions.Registration> registrations, SettingsTelemetryCollector telemetry)
    {
        if (!source.IsLoaded || !source.IsRegistered)
        {
            throw new InvalidOperationException("Register and load the source in configuration before resolving settings.");
        }

        _source = source;
        _telemetry = telemetry;
        _telemetry.DocumentLoaded(source.Result.Status, source.LoadDurationMilliseconds, source.LoadException);
        _services = services;
        _registrations = registrations.ToArray();
        _document = source.CopyDocument();
        _protected = source.Result.Status is not (SettingsLoadStatus.Loaded or SettingsLoadStatus.Defaults);
    }

    internal object Sync { get; } = new();

    internal SemaphoreSlim Writes { get; } = new(1, 1);

    internal SettingsLoadResult LoadResult => _source.Result;

    internal Exception? LoadException => _source.LoadException;

    internal bool IsProtected => _protected;

    public void ValidateRegisteredSettings()
    {
        foreach (PersistedOptionsExtensions.Registration registration in _registrations)
        {
            registration.Resolve(_services).EnsureInitialized();
        }
    }

    public async Task<SettingsDocumentSaveResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        using SettingsTelemetryCollector.Operation operation = _telemetry.Begin("document-reset", "$document");
        try
        {
            SettingsDocumentSaveResult result = await ResetValueAsync(operation, cancellationToken).ConfigureAwait(false);
            operation.Complete(result.Status.ToString());
            return result;
        }
        catch (OperationCanceledException)
        {
            operation.Complete("Cancelled");
            throw;
        }
        catch (Exception error)
        {
            operation.RecordException(error);
            throw;
        }
    }

    public void Dispose() => Writes.Dispose();

    internal JsonNode? ReadSection(string sectionId) => _document["sections"]![sectionId]?.DeepClone();

    internal bool ContainsSection(string sectionId) => ((JsonObject)_document["sections"]!).ContainsKey(sectionId);

    internal JsonObject ReplaceSection(string sectionId, JsonObject section)
    {
        var next = (JsonObject)_document.DeepClone();
        next["sections"]![sectionId] = section;
        return next;
    }

    internal async Task StoreAsync(JsonObject replacement, string sectionId, CancellationToken cancellationToken)
    {
        using SettingsTelemetryCollector.Operation operation = _telemetry.Begin("store", sectionId);
        try
        {
            await _source.Store.WriteAtomicallyAsync(Serialize(replacement), cancellationToken).ConfigureAwait(false);
            operation.Complete("Saved");
        }
        catch (OperationCanceledException)
        {
            operation.Complete("Cancelled");
            throw;
        }
        catch (IOException error)
        {
            operation.RecordException(error);
            operation.Complete("StorageFailure");
            throw;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
        {
            operation.RecordException(error);
            operation.Complete("ValidationFailed");
            throw;
        }
        catch (Exception error)
        {
            operation.RecordException(error);
            throw;
        }
    }

    internal void Publish(JsonObject replacement)
    {
        _document = replacement;
        _source.Publish(replacement);
    }

    private static byte[] Serialize(JsonObject document) => SettingsJson.SerializeDocument(document);

    private async Task<SettingsDocumentSaveResult> ResetValueAsync(SettingsTelemetryCollector.Operation operation, CancellationToken cancellationToken)
    {
        ValidateRegisteredSettings();
        (ISettingsSlot Slot, (object Value, JsonObject Section) Prepared)[] defaults;
        try
        {
            defaults = _registrations.Select(registration => registration.Resolve(_services)).Select(slot => (Slot: slot, Prepared: slot.PrepareDefaults())).ToArray();
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
        {
            operation.RecordException(error);
            return new(SettingsSaveStatus.ValidationFailed, [error.Message]);
        }

        await operation.WaitAsync(Writes, cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject replacement = PersistedSettingsSource.EmptyDocument();
            var sections = (JsonObject)replacement["sections"]!;
            foreach ((ISettingsSlot Slot, (object Value, JsonObject Section) Prepared) entry in defaults)
            {
                sections[entry.Slot.SectionId] = entry.Prepared.Section;
            }

            try
            {
                await StoreAsync(replacement, "$document", cancellationToken).ConfigureAwait(false);
            }
            catch (IOException error)
            {
                operation.RecordException(error);
                return new(SettingsSaveStatus.StorageFailure, [error.Message]);
            }
            catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
            {
                operation.RecordException(error);
                return new(SettingsSaveStatus.ValidationFailed, [error.Message]);
            }

            lock (Sync)
            {
                _document = replacement;
                _protected = false;
                _source.Publish(replacement);
                foreach ((ISettingsSlot Slot, (object Value, JsonObject Section) Prepared) entry in defaults)
                {
                    entry.Slot.Commit(entry.Prepared.Value, recovering: true);
                }
            }

            return new(SettingsSaveStatus.Saved, []);
        }
        finally
        {
            Writes.Release();
        }
    }
}
