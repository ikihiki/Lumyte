# Lumyte.Input.Settings

Optional adapters for `Lumyte.Settings`. The processing and action cores do not
depend on the settings service.

Register the shared persistence source with `AddSettings`, then call
`AddInputSettings`. The `input-processing` and `input-actions` sections have
independent revisions, validated writable DTOs and source-generated JSON metadata.
Enum tokens are persisted by name. Runtime device IDs, held controls, active
contexts, recognizer state and buffered operations are excluded.

`InputSettingsCoordinator.ApplyCommittedSettings` polls revisions on the input
thread and queues replacements before InputSystem.Update/ActionSystem.Advance.
`SaveRebindAsync` persists a frozen candidate without calling Confirm. Only a
successful committed revision is applied on the next tick. Failure leaves the
live profile unchanged. Preserve and display SettingsSaveResult diagnostics.
Rebind saves must originate from the currently applied settings profile. A newer
revision queued for the next tick already makes older captures conflict, and
applying that revision does not make the older capture valid again.

Defaults select calibration profiles by device kind names. A `selectProfile`
callback can map device descriptors to stable user profile names. Processing
settings also store touch dimensions and independent stick smoothing constants.

See [the runnable sample](../../../samples/Lumyte.Input.Advanced.Sample/README.md).
