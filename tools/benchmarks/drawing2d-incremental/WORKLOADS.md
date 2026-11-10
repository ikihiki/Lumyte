# 2D・ゲームUI・エディタUIの独立benchmark suite

シナリオの拡張、公開保持APIとの比較、画素検証と計測境界の共通化は[ADR-GRAPHICS-0027の測定設計案](../../../docs/adr/graphics/GRAPHICS-0027-scenario-based-graphics-benchmarks.md)を参照してください。以下は現在実行できるsuiteの手順です。

既存のBenchmarkDotNet 0.15.8とRadeon timestamp probeを再利用する。矩形保持試作だけを3用途の代表とせず、実際に異なる更新・layout・文字・記録の負荷を個別に選ぶ。

## 起動

repo rootで実行する。

```bash
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh build
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh list
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh smoke --suite all --count 32
```

buildは計測より先に完了する。他のbuild/testと並行測定しない。
wrapperはrepo指定SDK/native環境をactivateし、JIT／tiered PGO／GCの製品既定値を変更しません。
`LUMYTE_ENV_ROOT`の明示指定を維持し、診断用runtime環境変数は呼出し側の設定を継承します。
従来のtiering無効の結果は別系列です。

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

測定対象は集約`CpuFrame`と、独立した`Update`／`Layout`／`Record`です。GPU・Presentは含みません。
2Dには実layoutがないため空のLayout benchmarkを登録しません。
UIのLayoutは文字組みを内包し、Update中にもreconcile／text処理があり得ます。
静止caseのclean／cache経路は意図したfixture policyです。
集約の正式jobは5 launches、5 warmup、15 iterations、250 ms目標、outlier除去なしです。

工程別はBDN 0.15.8の`IterationSetup`と`InvocationCount(1, 1)`で一操作ずつ測ります。
事前stage・入力更新・counter／snapshot検証は計測外です。繰返し呼出しが無変更cacheへ退化するのを検出します。
**工程別は単発帰属診断であり250 ms iterationの正式throughput jobではありません。**
工程の独立費用は加算可能なE2E分解ではありません。pure compile／packは現行公開境界から分離できず、
新しい製品APIは追加せず、実runnerの非同期Prepareに含めて報告します。
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

## 目標寸法とhost処理区間

既定targetは従来どおり1280×720です。`--target-width 1920 --target-height 1080`でattachment寸法を明示できます。
実際の寸法・DPR 1・Rgba8Srgbをactive-device行へ出力し、readbackも同寸法を使います。
UIの論理viewportやfixtureの可視件数は変更しません。寸法だけで全ADR profileへの適合を主張しないでください。

`hostFrameMs`はupdate開始からSubmit復帰までの実測壁時計時間です。工程p95の和ではなく、
完了待ち・破棄を含む既存の`frameCpuWallMs`とも異なります。GPU timestamp区間は初期target遷移の後から、
記録されたupload・fallback clear・draw・layer間barrierまでです。Prepare内部の仕事は区間外です。
背景とHUDを分けたqueryや実hostの固定epoch期限は追加していません。

ADR準拠の反復でも製品既定runtimeを使い、診断overrideを付けた結果とは混ぜません。
独立processのA/A、固定warmup、5組の交互A/B、各2000 frames以上かつ10秒以上、正しさ検証が別途必要です。
短い既存の結果は診断であり、1920×1080の達成結果へ読み替えません。

## 毎回の安定性qualification

正しさ→固定上限pilot→事前宣言したA/A雑音幅→固定A/Bの順に進めます。
安定は物理環境の恒久保証ではなく、その実行の最後の4区間の条件です。
失敗・query不正・未測定・unstableをpassへ置換しません。

stage runnerはwarmupも`phase=warmup`で保存し、measuredとはphase内の0始まりiterationで区別します。
frame観測は事前確保した値型bufferへ保存し、JSON生成・counter辞書・command集計はhost/frame計測外です。
text counterはUpdate前と完了後の差なのでnested値であり、hostへ再加算しません。
Prepare／Encode／Submit／完了壁時計／退役を保持し、GPU queryをCPU待機へ置換しません。
queryは有限かつ正値を要求します。pipelineは別のthroughput診断で、この集計器の資格対象外です。

environmentとsource/harness/binary/font/shader hashをignored `artifacts/`へ保存し、同一入力・同一profileで実行します。
active-deviceはbuild時の全harness C#／project hashと実行DLL hashを持ち、dirty/untracked実装も識別します。
sourceラベルはcheckoutの申告値なので、それだけでbinary由来を証明せず、保存したhash／diffと照合します。
`unrecorded`等のsourceではA/A／正式資格を拒否します。

A/Aは少なくとも5独立process、例の許容値は**実行前に決める値**であり自動推奨ではありません。

実行例:

```bash
export LUMYTE_ENV_ROOT=/path/to/existing/dev-env
bash tools/benchmarks/drawing2d-incremental/run-workloads.sh hardware --case editor-scroll-list --count 32 --warmup 2000 --samples 200 --target-width 1920 --target-height 1080 > artifacts/pilot.jsonl
python3 -B tools/benchmarks/drawing2d-incremental/qualify-workloads.py pilot --warmup-frames 2000 artifacts/pilot.jsonl
python3 -B tools/benchmarks/drawing2d-incremental/qualify-workloads.py aa --warmup-frames 2000 --relative-threshold .05 --host-absolute-ms 1 --gpu-absolute-ms 1 artifacts/aa-{1,2,3,4,5}.jsonl
python3 -B tools/benchmarks/drawing2d-incremental/test-qualify-workloads.py
```

周期負荷は実行前に`--cycle-frames`を50の倍数へ固定し、同位相の4周期を比較します。
200〜2000の固定warmupを50刻みで選び、途中の最良窓で最終unstableを上書きしません。
正式A/Bは各2000 measured frames以上かつ10秒以上、5組以上のAB／BA交互順序、
共通frame数・trace・warmupとqualified raw A/Aを要求します。

`formal --help`に完全な指定形式を示します。集計器だけでは正しさ、process独立性、事前宣言の真実性は証明できません。
GPU readbackの非空checksumを独立golden正解とは扱わず、既存exact-pixel検証と区別します。

### 2026-11-10のハーネス実行確認

Radeon 8060S／RADV Mesa 26.0.8、SDK 10.0.403／runtime 10.0.14、製品既定runtimeで直列実行しました。
事前固定したcount 32、1920×1080、warmup 2000、measured 200、50-frame窓×最後4区間の診断です。

- editor-scroll-list: host中央値比1.14867でunstable、GPU比1.00115でstable。
- 2d-transform-all: host比1.17498でunstable、GPU比1.00646でstable。
- editor-scroll-listの独立5process A/A: host中央値range 6.2653 ms／12.92%で不合格。
  GPU range 0.29282 ms／0.4775%は事前宣言した1 ms／5%内でしたが、全processのhost warmup安定を満たしません。
- よって正式A/B資格はブロックしました。GPUだけの安定を全体のpassにせず、安定するまでの再試行はしていません。

変更前の同case・200 measured framesの4窓host比は1.26245、GPU比1.00148でした。
変更前warmupの観測はないため、変更後pilotと同等の正式比較や改善率とは扱いません。
buffer事前確保、frame内の匿名object／counter辞書生成除去、同一warmup計測経路で避けられる計測器雑音を減らしましたが、
この結果から共有host変動や製品側の割当・GC・driver動作の原因を一意には特定していません。

Release buildは警告0／error 0、Drawing2D単体157件成功・skip 0、全12caseのCPU smoke成功、
既存の実GPU exact-pixel照合59件成功。BDN quickはUIの4方法と2Dの3方法で各3正値sample、
非ゼロTotalOperationsとMemoryDiagnoser値を確認しました。最後に現sourceのUI工程と非整列1279×719 readbackを再実行しました。
単発工程probeの数値は正式性能順位に使いません。qualification／analyzer／CLIの決定的テストも実行しています。

raw JSONL、full BDN JSON、失敗preflight、固定plan、source／binary／font／shader hashはignored`artifacts/stability-20261110/`へ保存しています。長いcampaignは保存済みfrozen binaryに結び付け、後続の例外時cleanup修正と診断category追加を
source-amendmentへ記録しました。最終sourceと同一と偽らず、最終sourceのbuild／smokeを別証跡にしています。
これはheadless確認であり、実host期限、Present、物理scanout、全ADR目標達成は未測定です。
