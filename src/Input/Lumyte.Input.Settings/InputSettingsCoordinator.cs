using Lumyte.Input.Actions;
using Lumyte.Input.Processing;
using Lumyte.Settings;

namespace Lumyte.Input.Settings;

/// <summary>Applies committed settings at update boundaries and persists rebind candidates.</summary>
/// <remarks>Initializes a new instance of the <see cref = "InputSettingsCoordinator"/> class.</remarks>
/// <param name = "processing">The processing value.</param>
/// <param name = "actions">The actions value.</param>
/// <param name = "source">The source value.</param>
/// <param name = "system">The system value.</param>
/// <param name = "virtualSource">Optional virtual source for persisted gesture dimensions.</param>
/// <param name="selectProfile">Optional stable device-to-profile selection.</param>
public sealed class InputSettingsCoordinator(IEditableOptions<InputProcessingSettings> processing, IEditableOptions<InputActionSettings> actions, CorrectingInputSource source, ActionSystem system, VirtualizingInputSource? virtualSource = null, Func<InputDeviceDescriptor, string>? selectProfile = null)
{
    private readonly IEditableOptions<InputProcessingSettings> _processing = processing;
    private readonly IEditableOptions<InputActionSettings> _actions = actions;
    private readonly CorrectingInputSource _source = source;
    private readonly ActionSystem _system = system;
    private readonly VirtualizingInputSource? _virtualSource = virtualSource;
    private readonly Func<InputDeviceDescriptor, string>? _selectProfile = selectProfile;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private ActionProfile? _actionProfile;
    private long _processingRevision = -1;
    private long _actionRevision = -1;

    /// <summary>Schedules changed committed settings on the input owning thread.</summary>
    public void ApplyCommittedSettings()
    {
        if (Environment.CurrentManagedThreadId != _thread)
        {
            throw new InvalidOperationException("Apply settings on the input owning thread.");
        }

        if (_processingRevision != _processing.Revision)
        {
            SettingsSnapshot<InputProcessingSettings> snapshot = _processing.Current;
            _source.SetProcessorFactory(InputSettingsConverter.BuildCorrections(snapshot.Value, _selectProfile));
            float radius = snapshot.Value.TouchRadius;
            float swipe = snapshot.Value.SwipeDistance;
            _virtualSource?.SetGeneratorFactory(descriptor => descriptor.Kind == InputDeviceKind.Touch ? new TouchControllerGenerator(radius, swipe) : null);
            _processingRevision = snapshot.Revision;
        }

        if (_actionRevision != _actions.Revision)
        {
            SettingsSnapshot<InputActionSettings> snapshot = _actions.Current;
            ActionProfile profile = InputSettingsConverter.BuildProfile(snapshot.Value);
            _system.ApplyProfile(profile);
            _system.SetBufferOptions(new InputBufferOptions(TimeSpan.FromSeconds(snapshot.Value.BufferLifetimeSeconds), snapshot.Value.BufferMaxEntries));
            _actionProfile = profile;
            _actionRevision = snapshot.Revision;
        }
    }

    /// <summary>Persists a captured candidate; application occurs on the next committed-settings poll.</summary>
    /// <param name = "session">The session value.</param>
    /// <param name = "policy">The policy value.</param>
    /// <param name = "cancellationToken">The cancellationToken value.</param>
    /// <returns>The result of the operation.</returns>
    public Task<SettingsSaveResult<InputActionSettings>> SaveRebindAsync(RebindSession session, RebindConflictPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        SettingsEdit<InputActionSettings> edit = _actions.BeginEdit();
        if (edit.BaseRevision != _actionRevision || !ReferenceEquals(_system.ExportProfile(), _actionProfile) || !ReferenceEquals(session.SourceProfile, _actionProfile))
        {
            return Task.FromResult(new SettingsSaveResult<InputActionSettings>(SettingsSaveStatus.Conflict, _actions.Current, ["Input settings changed while rebinding; apply the committed settings and capture again."]));
        }

        ActionProfile candidate = session.PrepareProfile(policy);
        InputActionSettings converted = InputSettingsConverter.ToSettings(candidate);
        edit.Value.Actions = converted.Actions;
        edit.Value.Bindings = converted.Bindings;
        edit.Value.Contexts = converted.Contexts;
        edit.Value.Recognitions = converted.Recognitions;
        return _actions.SaveAsync(edit, cancellationToken);
    }
}
