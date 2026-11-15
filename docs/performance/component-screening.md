# 非同期の部品性能スクリーニング

## 目的と範囲

通常の正しさのCIとは独立して、共有GitHub Actionsランナーで部品の退行候補を発見する。
短時間測定であり、正式な性能保証・マージ条件・他ライブラリとの比較ではない。
branch protectionへ必須チェックとして追加しない。通常のCI workflowは変更しない。
週次網羅、候補の追加確認、実GPU比較はこの段階の対象外とする。

基準mainにあるSignalsのBenchmarkDotNetケースを起点にする。
対応していない部品の変更は、代表ケースが通ってもその部品の退行なしを意味しない。
選択理由と未測定範囲はplanおよび結果に残す。

## 実行と予算

component-screening workflowをPR作成・更新・再オープン・ready時に実行する。
同じPRの古い実行は取り消し、最新のheadを優先する。
文書のみなら測定を省略し、共通設定・依存関係の変更は保守的に対象を広げる。
ケース数はパラメーター込みで数え、上限12件とする。

1ジョブはUbuntu 24.04 x64で最大30分、実行エンジンには最大900秒を与える。
準備・ビルドを済ませてから同じランナー上で基準版と変更版を直列測定する。
同じホストの他ジョブによる干渉は排除できないため、確定的な性能判定には使わない。
時間切れ、ビルド失敗、ケース欠損、SDK非互換は合格ではなく未完了・比較不能とする。

手動のworkflow_dispatchは同一SHAのA/A確認を行う。
これは配線とノイズを見るための確認であり、基準版に対する改善の証拠ではない。

## 比較契約

PRのbase SHAとhead SHAを固定して個別のarchive snapshotを作り、同じハーネス・入力・
測定設定で両製品版を測る。基準の別マシンでの過去スコアとは直接比較しない。
製品のJIT・PGO・GC既定値を維持し、Releaseの短縮BenchmarkDotNetジョブを使用する。
既存の通常ベンチマーク設定は置き換えない。
対象別のSignals.Benchmarkへ統合し、--screeningで短縮ジョブ、
--screening-smokeで12ケースの意味検証を選ぶ。重複ソースは持たず、このproject全体を
実行側checkoutから両製品SHAのarchiveへ同じように重ねる。通常ジョブと--retentionは維持する。

時間は中央値の相対差10%以上とケース別の最小絶対差の両方を超えたら退行候補とする。
単一launchの候補抽出であり、統計的に確定した退行とは表示しない。
割り当て増加は時間と別に表示し、0 B/opからの増加も扱う。
未測定・未対応・不正なデータを「退行なし」に変換しない。
BenchmarkDotNetのiteration推定値を個別フレームの遅延分布とは呼ばない。

## 出力と調査

Job Summaryと30日保持のcomponent-screening artifactに、選択plan、比較SHA、
環境・ハーネス・実行ファイルの情報、生のBenchmarkDotNet出力と解析結果を保存する。
生成された測定データはGitへ追加しない。
再現用CLIとカタログの説明は、この文書の[Fallout commands](#fallout-commands)を参照する。

候補が出た場合は同じケース・入力・SDKで開発環境で再測定する。
共有ランナーの過去値だけから恒久的な性能差を断定しない。

## 通知と権限

測定workflowはcontents: readのみで実行し、checkoutに認証情報を残さない。
PRコードが実行される環境にコメント投稿権限やリポジトリのsecretを渡さない。

別のcomponent-screening-report workflowがdefault branchの信頼済みコードで通知する。
PR生成artifactの取得・展開・実行は行わず、GitHubのrun情報から安全な結果リンクだけを作る。
workflowの正常終了を「退行なし」とは表示しない。詳細はJob Summaryを正本とする。
PRへの関連付けが一意でなければ投稿を省略し、Actions上の結果を残す。

既存のbotコメントを更新し、投稿直前にPR headを確認する。
古いhead・古いrun・古い再実行が新しいコメントを上書きしないようにする。
マージ済みでも最後のheadと一致すれば結果リンクを残す。
head更新との完全な原子的更新はGitHubコメントAPIでは行えないため、対象SHAを必ず明記する。
通知workflowはmainへ入ってから有効になるため、導入PR内での通知動作は未検証である。

## 検証

投稿処理はネットワークなしの単体テストで、古い結果の排除、headの再確認、
bot以外のコメント保護、関連PRなし、失敗・キャンセル・タイムアウトの区別を確認する。

~~~sh
dotnet test tests/Tools/Lumyte.Build.Tests -c Release --filter FullyQualifiedName~ComponentScreening
~~~

測定・通知エンジンは既存のFallout runnerとC#テストに統合している。

## Operational guide

Advisory, CPU-only Signals microbenchmark screening, not an async UI component
benchmark or a full-suite qualification. The fixed base
70db1cd8144c22256505a433531efb0e37a4a7d0 has no benchmarks/Graphics suite.
Mathematics, Execution, native rendering, real async scheduling, cold startup,
and the other Signals workloads remain unmeasured. No native dependencies are
required by this harness.

## Fallout commands

Run with the exact SDK from global.json. No Python or native dependencies are
required. Fallout uses its normal SDK resolution; --dotnet-path selects an
explicit SDK executable. The runner archives exact plan SHAs directly from the
repository, so no shell worktree preparation is required. Dirty and untracked
product files are not measured. Optional --screening-base-directory and
--screening-head-directory accept read-only checkouts with matching HEAD SHAs.

~~~sh
dotnet run --project build -c Release -- --target ComponentScreeningPlan --screening-base BASE_SHA --screening-head HEAD_SHA --output artifacts/component-plan

# Force all twelve cases, including A/A:
dotnet run --project build -c Release -- --target ComponentScreeningPlan --screening-base SHA --screening-head SHA --screening-all true --output artifacts/component-plan-aa

dotnet run --project build -c Release -- --target ComponentScreeningRun --screening-plan artifacts/component-plan/plan.json --output artifacts/component-result --screening-timeout-seconds 900

dotnet test tests/Tools/Lumyte.Build.Tests -c Release --filter FullyQualifiedName~ComponentScreening

# Trusted CI notification, with credentials/event metadata in environment:
dotnet run --project build -c Release -- --target ComponentScreeningNotify
~~~

Use fresh run output directories. Both Release builds and all twelve semantic
smoke cases complete before serial base/head BDN measurement. Do not run other
builds or tests during measurement. One shared monotonic deadline covers all
commands and is capped at 900 seconds. Timeout kills the child process tree;
logs, raw partial files, result.json and summary.md survive ordinary failures.
On Linux, the standard /usr/bin/setsid utility and libc group kill also terminate
descendants retaining pipes after the direct child exits. Elsewhere .NET tree
kill handles live descendants and cancellable reads bound retained-pipe cleanup.
Filesystem exhaustion or forced host termination may prevent final reporting.
There are no automatic retries or weekly runs.

The plan target diffs the exact two commits (not their merge base), with
--no-renames and NUL-delimited paths. --repository-root can select another source
repository. GITHUB_OUTPUT receives selected, base_sha and head_sha. Documentation
and empty diffs produce skipped reports; --screening-all true overrides the skip.
Signals/common dependency changes select twelve cases; unknown changes select
ReadValue only and explicitly preserve uncovered paths, including mixed changes.
GITHUB_STEP_SUMMARY is appended by the C# reporter without shell orchestration.

## Frozen workload and job contract

Benchmarks use target-category layout, mirroring tests, with singular project
suffix: benchmarks/Core/Lumyte.Signals.Benchmark and
benchmarks/Core/Lumyte.Mathematics.Benchmark. Screening directly uses the existing
Signals ScalarBenchmarks, AsyncBenchmarks and GraphBenchmarks; there is no generic
screening project or duplicated Sources directory. Workload methods and lifecycle
remain unchanged from the fixed base except for the singular namespace.

The runner overlays the **whole same Signals.Benchmark project** from its own
checkout into both archived product revisions, discarding any archived target
harness first. It never uses each revision's changing benchmark implementation.
The ../../../src project reference resolves in both old-layout base archives and
new-layout revisions. BenchmarkDotNet remains 0.15.8. --screening explicitly selects
the short SignalsScreening-v2 job; --screening-smoke verifies all 12 cases without
timing. Without either flag the normal Signals job and --retention operation are
preserved. Harness version component-screening-v3, schema version 2, case version 2, singular case
identities and the v2 smoke marker reject old plans/results. Regenerate plans when
changing a workload or comparison policy.

The separate screening job uses 1 launch, 3 warmups, 8 measured iterations,
150 ms target, MemoryDiagnoser, full JSON, and OutlierMode.DontRemove.
Parser validation rejects missing/invalid/NaN data, missing allocations, unexpected
or duplicate identities, fewer than 8 retained results, job/parameter mismatch,
and incompatible runtime/CPU/SDK identities. Different global.json SDK policies,
unavailable exact SDK, or plan/harness mismatches are incomparable, never a pass.

The catalog is 12 unparameterized existing methods. Absolute latency floors in ns:

- Scalar ReadValue 1, ReadPeek 1, WriteSame 1, WriteUnobserved 2.
- Async CreateCompleted 20, ReloadCompleted 20, ReloadCompletedObserved 30,
  SupersedeAndCancelPending 100.
- Graph DiamondUpdate 20, SwitchBranch 20, CaptureEightReads 20, CheckReadSet 5.

A latency candidate requires median increase >=10% **and** its absolute floor.
These are pragmatic screening floors, not calibrated noise bounds or formal
statistical significance. Every allocation increase is separately flagged;
zero-base allocation increases have a null percentage and explicit bytes/op.
Candidates exit zero (advisory); execution, unsupported, incomparable, invalid or
missing data exits nonzero. no_candidates means only no candidate in these cases,
not general performance health. Unknown changed paths add `partial_coverage` to the
candidate/no-candidate status and remain explicit in the summary.

## Evidence and CI integration

The result includes exact SHAs and plan, case/harness versions and hash, runner
hash, CPU/OS, SDK and runtime dotnet --info, timing-related DOTNET_/COMPlus_
environment, and SHA-256 hashes of built harness/product/dependency DLLs.
BDN full JSON adds actual runtime/host identity and all iteration measurements.
Archive result.json, summary.md, *.log and raw/ even after a nonzero exit; scratch/
is useful for local diagnosis but need not be uploaded. Plan and raw paths are
local evidence, not trusted input to privileged workflows. GitHub workflow summary
rendering and any notification are owned by the surrounding CI workflow.

The original v1 exact-base A/A integration check completed all 12 cases on both sides
with the frozen harness, exact SDK 10.0.403, and 8 retained measurements per case.
It produced no candidates and identical allocation values. This verifies the
pipeline, not stability or a product improvement. Generated local evidence is
ignored under artifacts/component-screening-aa/ and is not committed.

The v2 target-project integration A/A check also completed against exact product
SHA 70db1cd8144c22256505a433531efb0e37a4a7d0 on both sides, using SDK 10.0.403.
All 12 singular case identities had 8 retained measurements per side, with
identical allocations and no candidates (91.46 seconds for the full engine).
Both semantic smokes passed before timing. Evidence is ignored under
artifacts/naming-integration/aa-result/. This is pipeline wiring verification,
not calibrated noise or evidence of product improvement; Mathematics is built
but is not measured by this Signals-only screening catalog.

### Fallout v3 integration evidence

The C# Fallout targets completed an exact-base A/A against
70db1cd8144c22256505a433531efb0e37a4a7d0 on both sides with SDK 10.0.403.
All twelve cases retained eight measurements per side, with identical allocations
and no candidates. Both builds/smokes completed before base then head timing.
The complete engine took 94.11 seconds. Local ignored evidence is under
artifacts/fallout-screening-port/aa-plan and artifacts/fallout-screening-port/aa-result.
This verifies wiring only, not calibrated noise, stability, or product improvement.

Plans/results now use schema version 2 and engine component-screening-v3; case
version 2 and the unchanged SignalsScreening-v2 BDN job/smoke contract are retained.
Old engine plans are rejected rather than silently reinterpreted. The frozen
harness is copied once into scratch/frozen-harness and overlaid identically into
both exact product snapshots. Screening targets have no Compile/full-solution or
native target dependencies. Snapshot local NuGet feeds are created in C#.

After adding deadline-cancellable archive extraction, the final runner was checked
again in artifacts/fallout-screening-port/aa-final: 92.91 seconds, all twelve
cases with eight samples per side, identical allocations and no candidates.
The earlier 94.11-second run is preserved separately; neither is a product
improvement claim. All 88 existing and added Build.Tests passed with zero skips.

### 型階層によるケース識別

現在のスクリーニングengineは`component-screening-v5`。12ケースの操作・入力・閾値・jobとsmoke markerは維持し、
型名を`Values.ScalarBenchmarks`、`Async.AsyncBenchmarks`、`Computed.GraphBenchmarks`の名前空間階層へ更新する。
古いengine／旧ハーネスのplanは拒否し、現在のカタログから再作成する。
測定目的以外の機能・範囲・操作性質のカテゴリは使わず、通常の型／メソッドglobで選ぶ。
