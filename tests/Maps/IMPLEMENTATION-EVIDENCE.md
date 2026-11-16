# M1/M2 implementation evidence

Owned checkout: /home/hideaki/.cache/lumyte-maps-implementation.
M1 branch feat/maps-m1-local-mvt, commit 7b6e89879b7f8dddee19d9b03c40f870ae4b42c7.
M2 branch feat/maps-m2-fixed-ui starts from that exact commit.

## Environment

SDK 10.0.403 from shared Lumyte dev-env, Release, unmodified analyzer/warnings gates.
LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env;
source tools/setup/environment.sh; prepend mise/data/installs/http-dotnet/10.0.403 and
mise/data/installs/http-slang/2026.19/bin to PATH. No tooling installation/reset or CI edits.
Actual GPU: Radeon 8060S Graphics (RADV STRIX_HALO), Mesa 26.0.8, Vulkan 1.4.335,
verified with shared sysroot vulkaninfo and /usr/share/vulkan/icd.d/radeon_icd.json.
Hardware identity raw log: /tmp/maps-vulkan-info.log.

## Executed gates

- Isolated M1 Release: 16 passed, zero skipped, /tmp/maps-m1-isolated.log.
- Integrated MVT Release: /tmp/maps-final-mvt.log (16 passed, zero skipped).
- UI record-only mount/reuse/replacement: /tmp/maps-final-ui.log.
- Session lifetime/admission plus actual hardware analytic polygon-hole/road pixels and direct versus
  MapView retained readback equality: /tmp/maps-final-session-gpu.log; earlier full 7-case hardware
  pass /tmp/maps-session-complete.log, parity /tmp/maps-retained-gpu.log.
- Real sample catalog tests: /tmp/maps-final-catalog.log; prior 17-case pass /tmp/maps-catalog-tests.log.
- Actual Release launcher: /tmp/maps-final-launcher.log. Debug launcher also exercised in
  /tmp/maps-launcher-run.log. Direct Release adoption smoke: /tmp/maps-adoption-smoke.log.
- Strict NativeAOT: /tmp/maps-aot-adoption-publish.log and /tmp/maps-aot-adoption-run.log;
  flags PublishAot=true, ILLinkTreatWarningsAsErrors=true, IlcTreatWarningsAsErrors=true.
- Strict trimmed: /tmp/maps-trim-adoption-publish.log and /tmp/maps-trim-adoption-run.log;
  PublishTrimmed=true, self-contained linux-x64, ILLinkTreatWarningsAsErrors=true.
- Markdown lint: /tmp/maps-markdown.log. Diff check: /tmp/maps-final-diffcheck.log.

Raw actual hardware RGBA readback: /tmp/lumyte-map-gpu-rgba.bin; PNG conversion
/tmp/lumyte-map-gpu.png (64 x 64), SHA256 ffbc90d4efc5b50d4f9d71e5a923d5af627b81b1b201b00fc442da86d7bb669b.
These artifacts are outside Git; no generated binary was added.

## Incomplete milestone acceptance

The implemented native vertical slices are usable, but the complete requested M2 acceptance is not
claimed: Browser execution and the broader DPI/parent-clip/overlay/edge GPU matrix remain incomplete.
These are explicit handoff gaps, not passing or silently skipped checks.

## Failures retained and limits

Initial strict analyzer failures and the incorrect test assumption that Rebuild adopts immediately are
retained in /tmp/maps-ui-tests-complete.log and earlier build logs. The regression was corrected to
exercise UpdateTree, the actual deferred publication boundary. No production gate was weakened.
One concurrent MSBuild worker-node resolver failure is retained in /tmp/maps-launcher-release.log;
final gates serialize builds with node reuse disabled, without resetting shared tooling.

Browser execution is blocked for this local-file-only adapter/sample: no Browser input/source or
browser map sample is implemented. Installed wasm-tools alone is not a compatibility proof.
The native AOT smoke verifies only the handmade fixture, not alternate CLR layout accounting.
No seam-free real-city claim, HTTP, M3 interaction/selection, parent fallback or performance claim.
Mixed zoom fixed sets are explicitly rejected. Broad DPI, parent clip/overlay and semitransparent
mixed-extent tile-edge GPU oracles remain incomplete; this evidence does not call those passed.
Decoder validates winding/groups, not self-intersections or geometric hole containment.
