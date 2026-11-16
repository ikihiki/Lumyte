# Browser sequential transition timeout investigation

## Scope and status

Investigation of PR #139 after merging main: topic `2969f76fb8fdd7a946b1e2290338b0b898688769`, exact main `c025240ae94b2b9538eec6e344897d5f9fd22a85`. This report does not claim a repaired CI run or a proven intermittent failure. No production rendering behavior, pixel assertion, timeout, retry, or known-timeout allowance was changed.

## Remote evidence

- Failing run: <https://github.com/ikihiki/Lumyte/actions/runs/43742344302/job/132898047597>. Sequential verification failed at `samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs:244`, the first connected-page PNG, after route/gate assertions. The reported state was `Running;Animating;` and the exception was `ManagedError: The automation deadline expired.`
- This is not the existing entry-midpoint exception. The existing allowance remains restricted to that earlier stage.
- Exact main run: <https://github.com/ikihiki/Lumyte/actions/runs/43737939045/job/132884571570>. Its actual Browser job log contains `ExitThenEnter UI transitions WebGPU passed`, not an ignored sequential timeout. Connected-page request was logged at 17:10:44.465 UTC and full sequential completion at 17:12:17.045 UTC.
- The failed topic requested the connected page at 18:00:53.429 UTC and failed at 18:01:24.116 UTC. These coarse log intervals do not isolate shader preparation, GPU completion, or readback.
- Both CI logs identify Chrome for Testing `156.0.8078.47`, .NET `10.0.403`, and the actual SwiftShader adapter.

## Local controls

Both revisions were published separately in Release with .NET `10.0.403` and Slang `2026.19`. Exact main used a detached, owned worktree. The local browser was the same pinned Chrome `156.0.8078.47`. `LUMYTE_REQUIRE_SOFTWARE_WEBGPU=1` was set and `LUMYTE_ALLOW_KNOWN_BROWSER_TIMEOUT` was unset. Every completed successful run verified the application-requested Google SwiftShader fallback adapter.

Host: Linux `7.0.0-42-generic`, AMD Ryzen AI MAX+ 395. This is not the GitHub-hosted CPU. Local Node was `24.22.2`, while the failed CI used `24.21.0`. No hardware performance conclusion follows from these software-GPU correctness runs.

The original sequential verifier is identical on topic and main (SHA-256 `4437dadffc1ad87fe48f092fa49d53a260d8c49ff4f210949f8689f231039f59`). Both exact, unchanged sequential runs passed all assertions locally. The initial topic run briefly overlapped a subsequently cancelled baseline build during startup, so it is not a timing comparison. Subsequent CPU-constrained runs were serialized with no concurrent builds.

For the constrained comparison, a temporary copy of that same verifier changed only import paths and wrapped `png()` in numbered start/elapsed logging. It retained the original capture expression, all assertions, sequencing, deadlines, and strict failure behavior. CPU affinity was restricted to CPUs 0 and 1 with `taskset`; this is a resource-sensitivity diagnostic, not a reproduction of the hosted CPU identity. Capture 19 is the original line-244 connected-page PNG.

## Results and attribution limit

- Unchanged original sequential verification: topic passed; exact main passed.
- Serialized two-CPU diagnostic: topic passed all 25 captures and assertions; exact main passed all 25 captures and assertions.
- Capture 3 (entry midpoint): topic 13,705 ms; main 13,838 ms.
- Capture 19 (the failing CI boundary): topic 15,736 ms; main 16,035 ms.
- Capture 20 (connected midpoint): topic 9,298 ms; main 9,281 ms.
- Capture 21 (connected endpoint): topic 11,986 ms; main 11,825 ms.

These are single-run diagnostic observations, not qualified performance measurements. The local connected-page boundary is expensive on both revisions and did not reproduce a topic-specific slowdown or the reported timeout. They do not establish that the remote failure is inherited or intermittent. The changed adapter hint and explicit Auto/Shared/DrawLocal selection remain intact; no speculative renderer repair is justified by this evidence.

The root cause remains unproven. The next discriminating evidence is a hosted-runner phase trace across preparation, submission completion and readback at capture 19 on both exact revisions, with the original deadlines and assertions retained. Local success alone is not a CI repair, and no remote rerun or publication was performed in this investigation.

Full successful local logs and numbered capture durations are archived in [the JSON evidence](results/browser-transitions-ci-43742344302.json).

## Reproduction

Use the repository toolchain and a pinned Chrome installation, then publish each revision in its own checkout:

```sh
dotnet publish samples/Lumyte.UI.Transitions.Browser.Sample -c Release -o artifacts/browser/ui-transitions
export LUMYTE_REQUIRE_SOFTWARE_WEBGPU=1
unset LUMYTE_ALLOW_KNOWN_BROWSER_TIMEOUT
node samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs artifacts/browser/ui-transitions/wwwroot --sequential
```

The constrained diagnostic adds `taskset -c 0,1` before Node. It must not be interpreted as permission to broaden the CI exception or adjust any deadline.

Raw local logs are retained in `/tmp/pr139-transitions-original-sequential.log`, `/tmp/pr139-base-original-sequential.log`, `/tmp/pr139-topic-2cpu-sequential.log`, and `/tmp/pr139-base-2cpu-sequential.log`. Exact successful-main Browser job evidence is `/tmp/pr139-main-browser-job.log`; the failed topic log is `/tmp/pr139-sync-ci-failed.log`. These temporary files are not durable evidence unless separately archived.
