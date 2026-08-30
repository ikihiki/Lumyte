# Lumyte DevTools MagicOnion demo

The Demo game Host is a .NET Generic Host with no ASP.NET Core dependency. It connects outward as a MagicOnion StreamingHub client over local gRPC/HTTP2 named pipes. The Server alone owns ASP.NET Core, Kestrel, WebSocket, and browser assets.

Start both processes in either order:

```powershell
dotnet run --project Lumyte.DevTools.Server --no-launch-profile
dotnet run --project Lumyte.DevTools.Host --no-launch-profile
```

Open `http://localhost:5198`. The Kestrel browser endpoint is localhost TCP; MagicOnion listens separately on the `Lumyte.DevTools` named pipe. Override the pipe with Server option `--DevTools:PipeName=MyPipe` and Host environment variable `LUMYTE_DEVTOOLS_PIPE_NAME=MyPipe`. Host identity uses `LUMYTE_DEVTOOLS_HOST_ID` and `LUMYTE_DEVTOOLS_DISPLAY_NAME`.

The Agent recreates its gRPC channel and StreamingHub connection after disconnect because channels using `SocketsHttpHandler.ConnectCallback` don't support gRPC connectivity-state tracking.

The Input page provides opt-in keyboard/mouse capture (with optional pointer lock), raw source and action-route diagnostics. The Resources page provides a typed catalog and accessible dependency tree with Load, Reload, Unload, and Collect controls. See `Lumyte.DevTools.Host/README.md` for detailed operation and safety behavior.

## Workspace navigation

The browser UI is organized as a desktop workspace rather than one long dashboard:

- **Overview** summarizes the connected host, protocol size, Input state, Resources state, and demo counter.
- **Input** provides Monitor, Action Maps, Browser Capture, and filtered Event Log tabs.
- **Resources** provides Dependency Tree, Details & Commands, and Operations tabs. Selecting a tree node carries its key into the details/command workspace.
- **Events & Protocol** isolates the cross-domain event stream and generic protocol contracts from the task-focused tools.

The current category and subtab are stored in the URL hash, so refresh and copied links restore the same view. Leaving Browser Capture stops capture and releases virtual input before navigation. The sidebar compacts near 1024 px and becomes an explicit drawer on narrow screens.

## Diagnostics workspace

The Diagnostics category displays the Host's bounded `System.Diagnostics` activity and metric collection. Use Metrics for semantic series aggregation and sparklines, Activities for timing/status/tag/event inspection, and Collector to pause/resume or clear retained telemetry and inspect capacity/drop counts. Collection defaults and overhead controls are documented in `Lumyte.DevTools.Host/README.md`.

## React frontend

The browser workspace lives in `ClientApp` and uses React 19, TypeScript, Vite, and Fluent UI React v9. Interactive controls come from Fluent UI; custom CSS is limited to the desktop shell, responsive pane sizing, capture surface, and SVG metric chart.

Requirements: Node.js 20.19 or newer and npm 10 or newer. From `ClientApp`, use `npm ci`, `npm run dev`, `npm test`, `npm run lint`, `npm run typecheck`, and `npm run build`. The Vite development server runs on port 5199 and proxies `/devtools` and `/health` to the ASP.NET server on port 5198. Production output is written to `wwwroot` and is rebuilt incrementally by `dotnet build`; `npm ci` runs only when the lockfile is newer than the installed dependency lock.

The client is divided into protocol transport/DTOs, shared state and operation tracking, reusable format/chart components, and Input, Resources, Diagnostics, Overview, and Protocol features. The runtime server and host never require Node.js; Node is a build-time dependency only.

### Connection and compatibility

The browser progresses through server connection, protocol negotiation, host selection, domain discovery, and host-connected states. It reports reconnection attempts, exponential backoff, last-message time, and disconnect reason. Protocol 1.0 negotiates `subscriptions`, `operations`, and `diagnostics-v1`; incompatible versions return a structured non-retryable error. Switching hosts closes the old socket, rejects pending requests, clears stale state, and establishes fresh subscriptions.

### Operations and safety

Queries and commands are recorded as running, succeeded, failed, or canceled with target and duration. Errors retain their protocol code, retryability, and details. Resource unload and collection require confirmation and show known references/dependencies. Input capture releases held inputs on Escape, navigation, surface/window blur, visibility loss, disconnect, and page unload. Form controls and global shortcuts remain outside the capture surface.

### Diagnostics and limits

Metric and activity updates are rendered only while their feature view is active. Metrics support meter/instrument/tag search, top-N selection, 1/5/15-minute windows, pause, current values, accessible SVG axes/hover values, and JSON export. Activities support text/status filtering and parent navigation. Collector values show only measured buffer usage and drop counters; no synthetic overhead estimates are displayed.

Client logs and command history are capped at 500 and 100 entries respectively. The server collector and protocol retain their separately documented bounds. Serialized diagnostic tags remain shallow scalar strings; unknown objects are represented by type name and long strings are truncated by the collector.

### Troubleshooting

If the page says **No runtime host**, start `Lumyte.DevTools.Host`. If protocol negotiation fails, rebuild the browser and server from the same checkout. If Vite cannot proxy WebSockets, confirm the ASP.NET server is listening on port 5198. Use the Events & Protocol command history for structured error details; Reconnect safely discards in-flight browser requests.

### Dependency policy

`package-lock.json` is authoritative and `npm ci` is used for clean installs. The checked dependencies are MIT-licensed or similarly permissive and do not require a repository-specific third-party NOTICE. `npm audit` is run without automatic fixes; breaking upgrades and `npm audit fix --force` are intentionally not used.