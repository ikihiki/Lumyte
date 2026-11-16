# Independent accounting/lifetime review

A read-only independent source review checked owner pooling, both scopes, saturation, mutable/reentrant fallback and ArgumentTablePlan size stability before timing. It identified one fault-injected exception-safety issue: adding to the frame list before collecting stable accounting could publish an unaccounted asset and make a retry revisit an earlier return.

The frozen implementation collects and attaches before publishing the list insertion, clears collection scratch on every exit, rolls back insertion failure and revalidates that an accounting callback did not release the frame. Already collected immutable owner arrays are reused on later transitions. Frame return moves accounting from active to idle without invoking the accounting callback again.

Evidence:

- `negative-insertion.log`: isolated pre-fix insertion order, one failed test; assertion saw two frame entries instead of one.
- `negative-fullscan.log`: disabling stable-owner opt-in retains the full-scan path; the one-collection guard fails with two accounting calls instead of one.
- `drawing2d-release.log`: final-source 411 passed, including both fixed insertion/reentrant-release regressions and the deterministic scope oracle.
- `baseline-drawing2d-release.log`: published PR #143 head has 403 passing tests.

The reviewer found no confirmed ordinary built-in path with lost/double accounting, premature pool reuse or pinned owner references. It checked that low-level statistics are assigned once and collection cannot resume after materialization. Tests were run separately by the implementation owner, not by the reviewer.

The negative-control worktree is isolated from the timed baseline and candidate. Neither negative-control source is performance evidence. No source or harness changes are made during the frozen A1/B1/B2/A2 run.
