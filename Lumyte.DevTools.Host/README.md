# Demo game Host

This project is the ASP.NET-free demo game process. It uses only the .NET Generic Host (`Microsoft.Extensions.Hosting`) for dependency injection and application lifetime. It owns the `DevToolsHub`, registers the demo, Input, and Resources domains, and runs the MagicOnion named-pipe Agent as a `BackgroundService`.

ASP.NET Core, HTTP, WebSocket, and static browser assets belong exclusively to `Lumyte.DevTools.Server`.

Run the Server and Host in separate terminals (either startup order works):

```powershell
dotnet run --project Lumyte.DevTools.Server --launch-profile http
dotnet run --project Lumyte.DevTools.Host
```

Then open `http://localhost:5198`.

## Input capture

Focus the **Input capture surface** and choose **Start capture**. While capture is active and that surface owns focus, browser `KeyboardEvent.code` values and mouse buttons, movement, and wheel deltas are forwarded to the demo's real `ActionRuntime`. The mapping table in the page shows every supported browser-to-engine control mapping. Unknown keys/buttons are ignored and recorded in the event log. Repeated keydown events are forwarded with their repeat flag.

Pointer lock is optional and starts only after **Enter pointer lock** is selected. Stop capture, focus loss, page visibility loss, pointer-lock release, page unload, and WebSocket closure trigger release-all behavior; held keyboard and mouse buttons are released and transient axes return to zero. Browser shortcuts, selection, context menus, and scrolling are suppressed only inside the focused capture surface while capture is active.

The Raw devices, Action maps and bindings, Action state, and Event log panels show the complete path from browser source/control/value through binding to action value and phase.

## Resource graph and commands

Choose a typed key from the catalog field, then use **Load**, **Reload**, **Unload**, or **Collect**. Only the built-in `demo:` catalog is accepted; arbitrary types and reflection are not exposed. **Loaded roots** shows explicitly loaded/pinned resources, while **All loaded** shows the store graph. Expandable tree items display type, state, generation, memory, reference count, and parent-to-dependency edges. Shared dependencies appear as references instead of being recursively duplicated.

Unload is allowed only for an explicitly loaded root. Invalid or impossible operations return a structured code and message in the command result area. Operation start/success/failure events update the status, tree, and event log live.

Full pipe configuration details are in `Lumyte.DevTools.Server/README.md`.

## Native demo window

On Windows the Host creates a visible **Lumyte DevTools Input Demo** window on a dedicated STA thread using the repository's `WindowsPlatform`. Its `WindowsKeyboard` and `WindowsMouse` are adapted into the same `ActionRuntime` as browser injection, while snapshots and events retain `window/demo-window` versus `browser/browser` source identities. Native focus loss releases the adapter's held state. Closing the native window follows the platform convention: the final window makes `PumpEvents` return false and stops the Generic Host. Host cancellation disposes the window and platform on their owning thread.

## Diagnostics

The Host starts a transport-independent `DiagnosticsCollector` before the demo domains. It uses `ActivityListener` and `MeterListener` to subscribe only to source and meter names beginning with `Lumyte.`. Existing module operation names, tags, events, statuses, instrument names, units, and descriptions pass through unchanged; production modules do not reference DevTools.

The **Diagnostics** workspace has Metrics, Activities, and Collector tabs. Metrics separates counter/up-down current value, delta and rate from histogram count/sum/min/max/p50/p95, and renders a bounded series sparkline. Activities show completed and currently active operations, hierarchy IDs, duration, status, tags, baggage, and events. Collector shows buffer pressure and dropped counts and provides Pause, Resume, and Clear commands.

Default safety limits are 512 stopped activities, 2,048 retained metric samples, 256 tag-distinct series, and a 256-value histogram percentile window. Old data is evicted and drops are reported. Browser updates are coalesced to at most one bounded batch per second, so collection remains bounded when no UI is connected. Tag values are converted to shallow strings; unknown object graphs are represented only by type name. Collection can be paused at runtime, and the collector is disposed with the Generic Host. To disable it entirely in a production composition, omit `DiagnosticsCollector`/`DiagnosticsDomain` registration; the instrumented modules continue operating normally.

Set `LUMYTE_DEVTOOLS_DIAGNOSTICS_ENABLED=false` to keep the domain/status available without subscribing listeners, or set `LUMYTE_DEVTOOLS_DIAGNOSTICS_PREFIX` to change the explicit source/meter prefix filter.
