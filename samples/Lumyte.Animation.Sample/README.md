# Animation sample

Uses the existing Lumyte.Composition factories to construct a forward/reverse fade repeated twice. A ManualClock drives playback, and the consumer retrieves values and events after evaluation. No UI, bone structures, state machine or target callbacks are owned by the animation library.

```sh
dotnet run --project samples/Lumyte.Animation.Sample
```

The final update reports completion at 4 seconds. The `Cycle` marker is collected at 2 and 4 seconds.

The second example composes a segment with temporal and spatial Bezier curves,
reversed source time and a half-weight blend. `SourceTrack` builds every nested
source as part of the single `Timeline.Build()`. Its midpoint reports `25.0`.
No file loader or value application is required to evaluate these sources.
