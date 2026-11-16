# M6 地図 workload（source / correctness）

既存 BenchmarkDotNet 0.15.8 / net10.0 runner に追加。性能値・画像・raw report は公開しない。functional correctness と case discovery のみ。

## fixture v1

実OSMでなく完全手作りの合成MVT 2。HTTP、文字、ラベル、フォントなし。出典表示: Handmade synthetic geometry; no OSM。schema lumyte.roads-buildings-water.v1、extent 4096、XYZ (1,1,1)。DenseMapFixture.CreateBytes が唯一の入力生成器。
water=(0,0)-(4096,384)、建物16×16、原点(64+248*x,64+248*y)、幅高さ160。道路32本、y=32+126*i、x=0..4096。ID=i+1、属性なし。背景黒、水青、建物赤、道路白、線幅2 logical px、NonZero polygon。512×512、DPI 1、既定Drawing2D stroke/AA品質。SHA256（生成bytes）= 4BD7A92AFB49166C49E130A52C08F4B6BA403ED6B3E134B7B280B90D1A6255AD。DenseMapFixture.Hashとtestで固定。fixture sourceはDrawing2D.Benchmark/Maps、GPU exeからsource-link（別namespace）し、両exeは同じ生成式を使用。

path seed=0（乱数なし）。frame f: 基準cameraへ戻す→Pan((f%16)-8,(f%7)-3)→ZoomAt((256,256),(f%16)/32.0)。実M3 API使用、累積driftなし。GPU operation=f0..5の6frame、最終drain込み。

## 区間

- MvtDecoderDecode: 既生成bytesの実decode。hash計算はsetup。
- SourceAcquire: 実InMemoryMvtSource snapshot/open-read/dispose。decode、HTTPなし。
- StaticSessionRecord: warm fixed-set MapSession.Record。
- FixedPanContinuousZoomRecord: camera更新とrecord。cold streamingでない。
- WarmCpuPrepareCommandRecordDispose: CPU mock準備/command記録/破棄の複合区間。GPU実行でない。
- GPU MapFrames: session direct immutable reuseまたは実MapView/UiDrawingScene2D retained。prepare/command/Submit/実WaitAsync/Completed検証/retire/破棄を含む。depth1=CPU wall completion latency、depth3=独立target/view/prepared/command/submission slotの有界headless throughput。GPU timestamp、Present、FPSでない。
- pixel readback/analytic oracleはGlobalSetup/Cleanupのみ、計測外、画像保存なし。独立生成式から建物内部・水内部・未被覆背景の3点を検証し、移動時は既知pan/scaleを解析適用する。raster同士の差分を正解としない。
- MapStreamingBenchmarks: 実M4 spatial ctorとSetCamera/SetViewport/Advanceを使用。XYZ (1,0,1)→(1,1,1)→(1,0,1)、128×128 viewport、resident上限1、prefetch/fallbackなし。MvtResourceStageの実decode前gateと実release前gateを制御し、未到着でReadyVisibleTiles=0、publication後=1、退避中のcharge保持、Manager Trim後ReleasedGenerationCount=2を検証する。fixture生成/registration、需要reconcile、実取得/decode/publication、LRU退避、manager trim、最終cleanupを含むcold local複合区間。各phase単独時間と偽らない。fixed-set Advance no-opでもHTTP streamingでもない。

## 再現

SDK 10.0.403 / Slang 2026.19共有source環境をsourceして実行。

~~~sh
export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env
source tools/setup/environment.sh
export PATH="$LUMYTE_ENV_ROOT/mise/data/installs/http-dotnet/10.0.403:$LUMYTE_ENV_ROOT/mise/data/installs/http-slang/2026.19/bin:$PATH"
dotnet build benchmarks/Graphics/Lumyte.Graphics.Drawing2D.Benchmark -c Release
dotnet build benchmarks/Graphics/Lumyte.Graphics.Benchmark -c Release
dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Drawing2D.Benchmark -- --catalog --filter "*MapCpu*"
dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Benchmark -- --catalog --filter "*MapFrames*"
dotnet test tests/Graphics/Lumyte.Graphics.Benchmark.Tests -c Release --filter "FullyQualifiedName~MapWorkloadTests|FullyQualifiedName~CatalogTests"
dotnet test tests/Tools/Lumyte.Build.Tests -c Release --filter FullyQualifiedName~BenchmarkCatalogTests
~~~

catalogはdevice-free。GPU correctnessは既存opt-in LUMYTE_GRAPHICS_INTEGRATION_GPU=1とHardwareVulkanが検証する絶対hardware manifestを使用。VK_DRIVER_FILESを指定したvulkaninfo --summaryでdeviceを事前確認。GPU test skipをGPU証拠と見なさない。

将来の計測だけ共有runner --mode screen / compare / diagnose を使用。raw出力はgitignored artifacts/以下のみ。測定中build/testを並列実行しない。今回full timing campaignや新timer engineはない。

共有native libraryを使い再buildを避ける場合はtestに -p:LumyteBuildParleyNative=false -p:LumyteParleyNativeOutput="$LUMYTE_ENV_ROOT/../text-parley/native" を渡す（存在するlibraryを確認）。
GPU oracleの再実行例: LUMYTE_GRAPHICS_INTEGRATION_GPU=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet test tests/Graphics/Lumyte.Graphics.Benchmark.Tests -c Release --no-build --filter FullyQualifiedName~MapSlotsDrain

streaming catalog: dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Drawing2D.Benchmark -- --catalog --filter "*MapStreaming*"
