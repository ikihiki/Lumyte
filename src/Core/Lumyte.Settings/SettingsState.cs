using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;

namespace Lumyte.Settings;

internal sealed class SettingsState<T> : IEditableOptions<T>, ISettingsSlot
    where T : class, new()
{
    private readonly SettingsDocument _document;
    private readonly SettingsTelemetryCollector _telemetry;
    private readonly ISettingsDefinition<T> _definition;
    private readonly JsonTypeInfo<T> _metadata;
    private readonly IConfigureOptions<T>[] _configure;
    private readonly IPostConfigureOptions<T>[] _post;
    private readonly IValidateOptions<T>[] _validators;
    private readonly Lazy<Initial> _initial;
    private T? _value;
    private long _revision;
    private bool _protected;

    public SettingsState(string sectionId, SettingsDocument document, SettingsTelemetryCollector telemetry, ISettingsDefinition<T> definition, IEnumerable<IConfigureOptions<T>> configure, IEnumerable<IPostConfigureOptions<T>> post, IEnumerable<IValidateOptions<T>> validators)
    {
        SectionId = sectionId;
        _document = document;
        _telemetry = telemetry;
        _definition = definition;
        _metadata = SettingsJson.CreatePersistenceMetadata(definition.JsonTypeInfo);
        _configure = configure.ToArray();
        _post = post.ToArray();
        _validators = validators.ToArray();
        _initial = new Lazy<Initial>(Initialize);
    }

    public string SectionId { get; }

    public SettingsLoadResult LoadResult => _initial.Value.Result;

    public SettingsSnapshot<T> Current
    {
        get
        {
            EnsureInitialized();
            lock (_document.Sync)
            {
                return Snapshot();
            }
        }
    }

    public long Revision
    {
        get
        {
            EnsureInitialized();
            lock (_document.Sync)
            {
                return _revision;
            }
        }
    }

    public SettingsEdit<T> BeginEdit()
    {
        SettingsSnapshot<T> snapshot = Current;
        return new(this, snapshot.Revision, snapshot.Value);
    }

    public Task<SettingsSaveResult<T>> SaveAsync(SettingsEdit<T> edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!ReferenceEquals(edit.Owner, this))
        {
            throw new ArgumentException("The edit belongs to another settings service.", nameof(edit));
        }

        EnsureInitialized();
        return SaveObservedAsync(edit.Value, edit.BaseRevision, recovering: false, cancellationToken);
    }

    public Task<SettingsSaveResult<T>> ResetAsync(long expectedRevision, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return SaveObservedAsync(null, expectedRevision, recovering: true, cancellationToken);
    }

    public void EnsureInitialized() => _ = _initial.Value;

    public (object Value, JsonObject Section) PrepareDefaults()
    {
        T defaults = _definition.DeepClone(CreateDefaults());
        ImmutableArray<string> errors = NormalizeAndValidate(defaults);
        ThrowValidation(errors);
        defaults = _definition.DeepClone(defaults);
        return (defaults, PrepareSection(defaults));
    }

    public void Commit(object value, bool recovering)
    {
        _value = (T)value;
        _revision++;
        if (recovering)
        {
            _protected = false;
        }
    }

    private static void ThrowValidation(ImmutableArray<string> errors)
    {
        if (!errors.IsEmpty)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(T), errors);
        }
    }

    private Initial Initialize()
    {
        using SettingsTelemetryCollector.Operation operation = _telemetry.Begin("load", SectionId);
        try
        {
            Initial initial = InitializeValue(operation);
            if (_document.LoadException is Exception loadException)
            {
                operation.RecordException(loadException);
            }

            operation.Complete(initial.Result.Status.ToString(), 0);
            return initial;
        }
        catch (Exception error)
        {
            operation.RecordException(error);
            throw;
        }
    }

    private Initial InitializeValue(SettingsTelemetryCollector.Operation operation)
    {
        if (_definition.SchemaVersion < 1)
        {
            throw new InvalidOperationException("SchemaVersion must be positive.");
        }

        T defaults = CreateDefaults();
        T validatedDefaults = _definition.DeepClone(defaults);
        ThrowValidation(NormalizeAndValidate(validatedDefaults));
        validatedDefaults = _definition.DeepClone(validatedDefaults);
        lock (_document.Sync)
        {
            SettingsLoadResult result = _document.LoadResult;
            T value = validatedDefaults;
            if (!_document.IsProtected && _document.ContainsSection(SectionId))
            {
                try
                {
                    JsonObject section = _document.ReadSection(SectionId) as JsonObject ?? throw new JsonException("A section must be an object.");
                    if (section["schemaVersion"] is not JsonValue version || !version.TryGetValue<int>(out int number) || number < 1 || section.Any(pair => pair.Key is not ("schemaVersion" or "values")))
                    {
                        throw new JsonException("Invalid section envelope.");
                    }

                    if (number > _definition.SchemaVersion)
                    {
                        throw new NotSupportedException("Unsupported section schemaVersion.");
                    }

                    JsonObject values = section["values"] as JsonObject ?? throw new JsonException("Section values must be an object.");
                    JsonObject migrated = _definition.Upgrade((JsonObject)values.DeepClone(), number);
                    value = SettingsModelOverlay.Read(_definition.DeepClone(defaults), migrated, _metadata);
                    ImmutableArray<string> errors = NormalizeAndValidate(value);
                    if (!errors.IsEmpty)
                    {
                        throw new JsonException(string.Join(Environment.NewLine, errors));
                    }

                    value = _definition.DeepClone(value);
                    result = new(SettingsLoadStatus.Loaded, []);
                }
                catch (NotSupportedException error)
                {
                    operation.RecordException(error);
                    value = validatedDefaults;
                    result = new(SettingsLoadStatus.UnsupportedVersion, [error.Message]);
                    _protected = true;
                }
                catch (Exception error) when (error is JsonException or ArgumentException)
                {
                    operation.RecordException(error);
                    value = validatedDefaults;
                    result = new(SettingsLoadStatus.InvalidData, [error.Message]);
                    _protected = true;
                }
            }
            else if (!_document.IsProtected)
            {
                result = new(SettingsLoadStatus.Defaults, []);
            }

            _value = value;
            return new(result);
        }
    }

    private T CreateDefaults()
    {
        var value = new T();
        foreach (IConfigureOptions<T> configure in _configure)
        {
            if (configure is IConfigureNamedOptions<T> named)
            {
                named.Configure(Options.DefaultName, value);
            }
            else
            {
                configure.Configure(value);
            }
        }

        return value;
    }

    private ImmutableArray<string> NormalizeAndValidate(T value)
    {
        foreach (IPostConfigureOptions<T> post in _post)
        {
            post.PostConfigure(Options.DefaultName, value);
        }

        ImmutableArray<string>.Builder errors = ImmutableArray.CreateBuilder<string>();
        foreach (IValidateOptions<T> validator in _validators)
        {
            ValidateOptionsResult result = validator.Validate(Options.DefaultName, value);
            if (result.Failed)
            {
                errors.AddRange(result.Failures);
            }
        }

        return errors.ToImmutable();
    }

    private async Task<SettingsSaveResult<T>> SaveObservedAsync(T? value, long expectedRevision, bool recovering, CancellationToken cancellationToken)
    {
        using SettingsTelemetryCollector.Operation operation = _telemetry.Begin(recovering ? "reset" : "save", SectionId);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            T candidate = _definition.DeepClone(recovering ? CreateDefaults() : value!);
            SettingsSaveResult<T> result = await SaveCandidateAsync(candidate, expectedRevision, recovering, operation, cancellationToken).ConfigureAwait(false);
            operation.Complete(result.Status.ToString(), result.Snapshot.Revision);
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

    private async Task<SettingsSaveResult<T>> SaveCandidateAsync(T candidate, long expectedRevision, bool recovering, SettingsTelemetryCollector.Operation operation, CancellationToken cancellationToken)
    {
        ImmutableArray<string> errors = NormalizeAndValidate(candidate);
        if (!errors.IsEmpty)
        {
            return new(SettingsSaveStatus.ValidationFailed, Current, errors);
        }

        JsonObject section;
        try
        {
            candidate = _definition.DeepClone(candidate);
            section = PrepareSection(candidate);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
        {
            operation.RecordException(error);
            return new(SettingsSaveStatus.ValidationFailed, Current, [error.Message]);
        }

        await operation.WaitAsync(_document.Writes, cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject replacement;
            lock (_document.Sync)
            {
                if (_document.IsProtected || (_protected && !recovering))
                {
                    return new(SettingsSaveStatus.RecoveryRequired, Snapshot(), []);
                }

                if (_revision != expectedRevision)
                {
                    return new(SettingsSaveStatus.Conflict, Snapshot(), []);
                }

                replacement = _document.ReplaceSection(SectionId, section);
            }

            try
            {
                await _document.StoreAsync(replacement, SectionId, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException error)
            {
                operation.RecordException(error);
                return new(SettingsSaveStatus.StorageFailure, Current, [error.Message]);
            }
            catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
            {
                operation.RecordException(error);
                return new(SettingsSaveStatus.ValidationFailed, Current, [error.Message]);
            }

            lock (_document.Sync)
            {
                _document.Publish(replacement);
                Commit(candidate, recovering);
                return new(SettingsSaveStatus.Saved, Snapshot(), []);
            }
        }
        finally
        {
            _document.Writes.Release();
        }
    }

    private JsonObject PrepareSection(T value)
    {
        JsonObject section = ToSection(value);
        JsonObject values = section["values"] as JsonObject ?? throw new JsonException("Settings values must be a JSON object.");
        T restored = SettingsModelOverlay.Read(_definition.DeepClone(CreateDefaults()), values, _metadata);
        ImmutableArray<string> errors = NormalizeAndValidate(restored);
        if (!errors.IsEmpty)
        {
            throw new JsonException(string.Join(Environment.NewLine, errors));
        }

        if (!JsonNode.DeepEquals(values, ToSection(restored)["values"]))
        {
            throw new JsonException("Settings must round-trip without changing their persisted values.");
        }

        return section;
    }

    private JsonObject ToSection(T value) => new()
    {
        ["schemaVersion"] = _definition.SchemaVersion,
        ["values"] = JsonSerializer.SerializeToNode(value, _metadata),
    };

    private SettingsSnapshot<T> Snapshot() => new(_revision, _definition.DeepClone(_value!));

    private sealed record Initial(SettingsLoadResult Result);
}
