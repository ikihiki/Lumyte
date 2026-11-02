# Drawing2D部分更新の比較用CPU試作

2D・ゲームUI・エディタUIを用途別に測る場合は[独立workload suite](WORKLOADS.md)を参照してください。CPU-only、実GPU工程別、1/3 frame並行を個別に実行できます。

設計案を評価するための非配布ハーネス。製品のscene API/GPU cacheの実装ではありません。
BenchmarkDotNet 0.15.8と既存Graphics benchmarkの明示project generatorを再利用します。

## 比較するもの

- main `fbc8105` とPR105 `ffe075c` の実際のDrawing2D実装を、独立したworktreeで同じハーネスから参照。
- 比較のための製品差分はbenchmark assemblyへのInternalsVisibleToのみ。runtime処理の置換や擬似的なmain/PR105クラスは使いません。
- `LegacyFrame`: 同じCanvasを再利用し、変更frameでは全要素を再記録してEnd。静止frameでは完成済みDrawListを再利用。
- `RetainedCommit`: 変更されたfragmentだけCanvas/Endで記録し、immutableなchunk/treeへ差し替える。これはCPU scene公開までで、既存Rendererへ直接渡せる型ではありません。
- `RetainedWithFlatten`: 上記に加えて全fragmentをReplay/Endし、既存Rendererへ渡せるDrawListを作る。変化のないframeはこのflat DrawListも再利用。

3世代分のCPU rootを保持し、変更後も旧snapshotが使える状態を測ります。GPU frames-in-flightの計測ではありません。
入力の色変更は各方式で同じ。prototype独自のdirty集計・sort・COW費用も測定へ含めます。保持型に有利な全command走査や手作業のディープコピーをbaselineへ追加しません。

## Workload

1,000/10,000命令について静止、1要素、連続した1%、分散した1%、全要素の色を変更します。geometryと2色のimmutable paintは共通です。
基本比較の論理fragmentは1命令。内部COW pageの64 fragmentと、再記録をまとめる論理fragmentの粒度は別です。
ChunkBenchmarksは論理fragmentを1/16/64/256命令へ変え、再記録の増幅とmetadata負荷を比較します。
初期構築はConstructionBenchmarksで別測定します。

正しさ確認ではclipped rectangleとmixed path/circleも含め、命令・paint・変換・clip・描画順と旧snapshotを比較します。
型/clip参照で分かれる連続run数も出力します。これはCPUで観測したrun数で、GPU draw captureではありません。

## 実行

```bash
source tools/setup/activate.sh
dotnet build tools/benchmarks/drawing2d-incremental -c Release
dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --smoke
DOTNET_TieredCompilation=0 DOTNET_gcServer=0 dotnet run   --project tools/benchmarks/drawing2d-incremental -c Release --no-build --   --filter '*UpdateBenchmarks*' --artifacts artifacts/incremental-bdn
DOTNET_TieredCompilation=0 DOTNET_gcServer=0 dotnet run   --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --retention
```

worktreeでのLUMYTE_ENV_ROOTには既存環境の絶対パスを指定できます。新しいcheckoutでは先にartifacts/nugetを作ります。
全worktreeのbuild/正しさ検証を終えてから測定し、測定と他のbuild・testを並行実行しません。
main→PR105と逆順の独立runを保存します。1 launch・3 warmup・8 measured iterations、iteration目標150ms、outlier除外なしです。
中央値/p95はBDNのiteration推定値の分布で、個々のframe latencyのp95ではありません。

## メモリと限界

MemoryDiagnoserのAllocatedは1操作あたりのmanaged割当です。
RetentionProbeは独立した非BDN実行でfull GC後のlive heap差分を測り、初期構築割当と区別します。RSS、VRAM、native driverメモリではありません。

`--software-integration`は`VK_DRIVER_FILES="$LUMYTE_LAVAPIPE_ICD"`を必須にし、既存Rendererへ渡せるlegacy/retained+flattenを実行します。CPU更新・prepare・record/finish・submit・submit-to-completion・破棄を分離し、最後のtexture readback hashを比較します。1 frame in flightのsoftware Vulkan診断であり、GPU timestampや実GPU throughputではありません。cold renderer、warmup、warmを区別します。managed割当は非同期処理を含むprocess-wide値で、BDNの同期メソッド割当とは別です。

この試作は固定要素数のpayload置換だけを実装します。insert/remove/reorder、property scope、GPU compiled asset/resident page、RenderGraph差分、dirty rectangleは未実装です。
RetainedCommitだけの速度を、既存Rendererを含むframe全体の速度向上として扱いません。
実GPUの利用権限が得られない間は、実GPU性能の比較を完了したとは扱いません。software rendererの正しさ検証とhardware性能は区別します。

## 実GPUモード

`run-hardware.sh`はAMD ICDを固定し、別worktreeのmain/#105を同じsourceでbuild/check後、main→#105→#105→mainの順に実行します。各variantでlegacy/retained+flattenの順も反転します。`--hardware-integration`はsoftware ICDを拒否し、作成したVulkanDeviceの物理deviceを直接検査します。

GPU queryのためのreflectionは計測用helper内に閉じ、Vulkan backendの製品APIは変更しません。Radeon8060Sのvendor/device IDを照合し、active queueのtimestampPeriod/validBitsを記録します。timestamp非対応ならnullとして報告し、CPU完了待ちをGPU時間へ読み替えません。

serial stage probeは1,000/1,024/1,025/10,000矩形×静止/1要素/全変更。各caseはcold1＋warmup40＋warm120 frameです。TOP_OF_PIPE→BOTTOM_OF_PIPE queryはPreparedDraw2D.Recordが記録するclear/upload/barrier/renderのGPU経過時間で、初期target barrierは範囲外です。純粋なshader演算時間やCPU submit→完了時間とは異なります。

pipeline probeは1,000/10,000矩形、同じ変更条件、1/3 frame in flightを比較します。各slotがtarget・command・prepared・submission・query poolを独立所有し、完了確認後だけ再利用します。warmup41 frameの最終drain後に120 frameを測り、最終drainと解放もelapsedへ含めます。記録・prepare・submit・待機・query readを含むheadless throughputで、Present/VSync/表示FPSではありません。複数frameのtimestamp区間は重なり得るため、合計をGPU占有時間として扱いません。

各caseの最終readbackをlegacy/retained+flattenで比較します。software側の過去結果とはsample数・tiering・ハードウェアが異なるため、直接の速度比としては扱いません。
