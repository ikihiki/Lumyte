# 2D・ゲームUI・エディタUIの独立benchmark suite

既存のBenchmarkDotNet 0.15.8とRadeon timestamp probeを再利用する。矩形保持試作だけを3用途の代表とせず、実際に異なる更新・layout・文字・記録の負荷を個別に選ぶ。

## 起動

repo rootで実行する。

```bash
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh build
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh list
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh smoke --suite all --count 32
```

buildは計測より先に完了する。他のbuild/testと並行測定しない。
wrapperはrepo指定SDK/native環境をactivateし、tiering/PGOを無効、workstation GCへ揃える。既存の比較ハーネスのentry pointは維持する。

## Suiteとcase

### 2d

- `2d-static`: geometry/paint/完成済みDrawListを再利用。
- `2d-transform-all`: 全instanceの位置・回転を変更し、geometry/paint定義は共有。
- `2d-geometry-stream`: path geometryそのものを更新。transform更新だけとは区別。
- `2d-camera-pan`: map/pathを共有し、camera変換を更新。

Canvas2D/Renderer2Dの現行経路であり、textured sprite engineや新しいresident-instance経路を実装したとは扱わない。

### game-ui

- `game-hud-static-overlay`: 動く2D背景と静止HUDを別の順序付きlayerで合成。
- `game-hud-values`: 実Signalでscore/healthを変え、UiSurfaceが公開・layoutする。
- `game-menu-animation`: deterministicな論理tickでmenuの幅/spacingを更新し、実layoutを測る。

実UiSurface/Elementと、Parleyによる単一行TextBoxを使う。静的なUI記録の再利用は明示的なbenchmark adapter policyであり、製品hostに一般Element記録cacheが実装済みという意味ではない。

### editor-ui

- `editor-idle`: 未変更のpanel/文字/完成済み記録を再利用。
- `editor-line-edit`: 実TextBoxのcontrolledな文字列変更とParley layout。
- `editor-scroll-list`: 可視windowの行を更新し、clipと文字を描画。
- `editor-dock-resize`: viewport/panel幅の変更による実layout。
- `editor-live-preview`: 静止panelと動く2D preview layerを合成。

可視行数制限はbenchmark側の明示的なvirtualization policy。製品virtualized listや完全なdocking engineの実装ではない。
TextBoxは単一行で、複数行document editor・IME/OS入力・Undo全体を測るものではない。previewは2Dであり、3D rendererの性能ではない。

## CPU-only BenchmarkDotNet

```bash
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh cpu --suite 2d --count 1000 --artifacts artifacts/workloads/2d-cpu
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh cpu --suite game-ui --count 200 --artifacts artifacts/workloads/game-ui-cpu
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh cpu --suite editor-ui --count 200 --artifacts artifacts/workloads/editor-ui-cpu
```

`--case editor-line-edit`等で1ケースだけ選べる。
`--quick`はbuild/実行確認用の短いjobであり、正式な性能結論には使わない。

測定対象はUpdate＋Layout＋CPU Recordで、GPU・Presentは含まない。
MemoryDiagnoserはmanaged byte/op。native ParleyのメモリやVRAMは含まない。
中央値/p95はBDN iteration推定値の分布で、個々のframe latencyとは異なる。

## 実GPUの工程別測定

```bash
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh hardware --suite game-ui --count 200 --warmup 41 --samples 120 > artifacts/game-ui-stages.jsonl
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh hardware --suite editor-ui --case editor-line-edit --count 200 --warmup 41 --samples 120 > artifacts/editor-edit-stages.jsonl
```

wrapperがAMD ICDを明示し、実際に作成したRadeon deviceのvendor/device ID・timestamp対応を検査する。softwareへのsilent fallbackは拒否。

JSONLにinput update、layout inclusive、native text layoutのnested時間、CPU記録、GPU Prepare、command記録/Finish、Submit、CPU完了待ち、解放、frame壁時計、GPU query区間を分けて保存する。
text layoutはlayout/updateの内数であり、もう一度加算しない。
GPU queryは記録したupload/barrier/clear/renderの区間で、排他的なGPU占有時間ではない。

複数layerは先頭でClearし、後続をLoadして順に合成する。UIの静止と、更新背景への合成頻度を分離する。

## 1/3 frame in flightのthroughput

```bash
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh hardware --suite 2d --count 1000 --mode pipeline --frames-in-flight 1 > artifacts/2d-depth1.jsonl
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh hardware --suite 2d --count 1000 --mode pipeline --frames-in-flight 3 > artifacts/2d-depth3.jsonl
```

各slotがtarget・command・prepared owner・submission・query poolを専有し、完了後だけ再利用する。warmupをdrainしてから測定し、最後のdrainと解放もelapsedへ含む。
headless throughputであり、表示FPS・Present/VSync・入力→表示遅延ではない。

## 集計

```bash
python3 tools/benchmarks/drawing2d-incremental/analyze-workloads.py artifacts/game-ui-stages.jsonl > artifacts/game-ui-summary.json
python3 tools/benchmarks/drawing2d-incremental/analyze-workloads.py artifacts/workloads/2d-cpu/results/*full.json > artifacts/2d-cpu-summary.json
```

case名・count・scopeを分けて集計し、異なる用途同士を速度比で比較しない。同じcase/入力/scale/targetでbefore/afterを測る。
`count`はケースごとにworld object数や全リスト行数等を指す。可視行数・layout/shape call等の実counterも確認する。

## 正しさと限界

CPU smokeは各fixtureのinvariantと非空記録を確認する。GPU readbackは非空画素とchecksumを保存するための動作確認で、独立golden画像との一致検証ではない。
大きなcountの複雑path/文字はGPU予算や長い準備時間に達し得るため、まず小さいscaleで確認する。
フォントはrepoのDejaVuSans.ttfを明示して使い、OSフォントやネットワーク取得へfallbackしない。native Parleyが必要で、失敗時に矩形へ置換しない。

全て強制frame評価のheadless workloadで、OSのidle wakeup/省電力・event-driven host schedulingそのものは未計測。静止caseの安さを「OS frameを省略できた」と読み替えない。

## Public retained API integration measurement

See [README public API probe instructions](README.md#public-retained-api--actual-ui-adapter-probe).
`--public-retained-api` holds 1024 visible rectangle commands constant while
varying public DrawList content-node partitioning (1/16/256), static versus one
actual changed command, and independent full Canvas versus retained preparation.
The real UiSurface/CreateDrawingScene adapter additionally covers 100/1000/10000
rectangle leaves for CPU Update and LastRecordingCount; only 100/1000 enter GPU
Prepare/Record/readback. `--public-retained-ui-cpu` isolates that CPU profile.
Do not combine these observations with the older layer-adaptation UI fixture or
private WithPage mutation measurements. Measured source, raw archives, independent-run statistics and limits are in
[the public API review report](../../../docs/performance/retained-public-api-review.md).

CPU UI cases are flat Panel siblings (N+1 UI elements), while GPU UI cases use
Stack grids. Each row emits layoutKind; these are separate workload shapes.
The unchanged 512-asset default renderer budget can cause UI1000 cache misses.
