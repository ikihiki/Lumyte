# MapView UI adapter

MapView is a passive Widget with a required borrowed MapSession. It does not start I/O on declaration.
MapElement uses existing UI layout, mount/unmount, local OnRecord, invalidation and retained dependency
validation hooks. Layout updates only logical viewport demand; content must call Advance.

One session may be mounted once. Unmount unsubscribes and detaches but does not dispose; content owns
BeginStop and asynchronous retirement after GPU completion. Replacing a session on the same mounted
identity is rejected before publication. Retained cache reuse is element-local, not tile GPU deltas.

The adapter never begins/ends the canvas, prepares, submits, waits or presents. Local clip and canvas
state restoration are the session recorder's contract. See samples/Lumyte.Maps.Sample for the public
application integration and tests/Maps for explicit record-only versus hardware pixel coverage.
