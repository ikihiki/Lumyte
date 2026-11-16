# 2D描画方式の実装検証（2026-11-16）

[設計](../adr/graphics/GRAPHICS-0036-tiled-compute-drawing.md)の三方式を実装し、機能・資源寿命・GPU画素・NativeAOTを検証した記録です。速度の比較や高速化の証明ではありません。機械可読の結果・ログhashは[検証manifest](results/renderer-strategies-20261116.json)に保存します。

## 実装範囲

- 共通のIRenderer2D。標準Renderer2D、SparseStripRenderer2D、ComputeRenderer2Dは別の具象型です。Canvas/DrawList2Dは共通です。
- 標準方式は専用図形と文字・画像経路を保ち、曲線等の4×4 shape membershipをcomputeで生成します。paintとSrcOverは通常描画です。既存constructorはshader/custom互換経路を維持し、新方式にはCreateDefaultまたはoptions constructorを使います。
- CPU疎strip方式は形状判定と水平stripをCPUで作り、paintと合成をGPUで行います。
- GPU compute方式はraw curveから曲線・stroke、tile count/scan/fill、backdrop、昇順の描画候補、coverage/paint/合成を実行します。七つの物理storage bufferと128 Bのrootを使い、通常passでresolveします。
- RenderGraphへcopy/compute/raster/resolveを宣言し、DisplayLinearのEffectImageを既存ImageEffectsへ渡します。直接Loadは既存attachmentへSrcOverし、背景を上書きしません。
- rendererローカルの有限CPU shape/strip cache、完成確認後の排他的な保持GPU資産再利用、in-flight scratchの分離を実装しました。一般的なcross-frame CPU paint-program cacheはありません。

## 検証条件と範囲

実装・最終予算修正の比較基準は `86ef5d848b124241e9ae83ba53972abbcdbf605d` です。manifestには実行したtest sourceのhashも残し、文書だけの追加commitと区別します。

- SDK 10.0.403、Slang 2026.19、Release構成。
- Radeon 8060S、RADV STRIX_HALO、Mesa 26.0.8、Linux x64。Vulkan ICDをRadeonへ固定し、native wgpuのdriver/render nodeもtraceで確認しました。
- Drawing2D単体テスト: 501 passed / 0 failed / 0 skipped。
- RenderGraph単体テスト: 143 passed / 0 failed / 0 skipped。
- 実GPUの選択matrix: 19 passed / 0 failed / 0 skipped。Vulkanとnative wgpuの両方で新方式を実行し、上限拒否を成功扱いする分岐はありません。
- strict NativeAOTはILLinkTreatWarningsAsErrorsとIlcTreatWarningsAsErrorsを有効にpublishし、同じ実行ファイルでVulkanとwgpuを選んで三方式の画素を読み戻します。対象は曲線、paint、upload、compute/resolve、親Graph、Opacityの経路です。実行結果とbinary hashはmanifestで確認できます。

GPU matrixには曲線、穴、stroke、別送信で初期化したLoad先、4×4相関、backdropとしてのOpacity/Composite、二つの保持frame、画像crop、coverage mask、SDF/MSDF/MTSDF、meshの補間coverage、linear/reflect/radial/sweepを含めます。既存fixed-record、clip、画像経路の回帰も同じmatrixで確認しました。全製品test suiteを19件だけで代替する意味ではありません。

## 不具合を検出した検証

### 非一様変換stroke

`M(-1,0) Q(0,1)(1,0)`、width=6、butt/bevel、Scale(16,0.25)とTranslation(64,8)、128×16 targetを使います。独立4×4 oracleのpixel(32,8)はmask0x0FF0、premultiplied RGBA=(0.5,0.5,0.5,0.5)です。

修正前はVulkan/wgpuとも実際の11 compute pass後に透明0を返し、同じassertionが失敗しました。中心線だけでなく局所接線と幅を分割数へ反映し、N=385で同じassertionが成功しました。塗りの分割数は変更していません。

### 疎stripのcompiled paint所有者

64個の異なるmeshが同じ32768-stop gradientを使う入力では、source paintの共有と、各DrawingProgram2Dが所有するCPU配列の重複を区別する必要があります。16 MiB上限で、修正前のcold/warm二ケースは必要な拒否をせず失敗しました。

修正後は実際の新program所有者ごとに、Compileより前に保守的容量を累積予約します。cold/warmの拒否、拒否時のnative buffer生成0、真に同じprogramを共有する場合の予約一回を決定的なcounterで検証しました。GC割当量や時間を合否に使っていません。

## 再実行

リポジトリの環境を有効にし、buildとGPU実行を直列化します。GPUのreadbackと所有者の完了確認を含む機能検証で、benchmarkではありません。

```sh
source tools/setup/activate.sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
export LUMYTE_GRAPHICS_GPU_TESTS=1
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Drawing2D.Tests
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.RenderGraph.Tests
dotnet run --project build -c Release -- --target Compile --project tests/Graphics/Lumyte.Graphics.Tests
dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release --no-build --no-restore --filter "FullyQualifiedName~AlternativeRenderer|FullyQualifiedName~ComputeNonuniformStrokeOracle|FullyQualifiedName~StandardCurveGraphOracle|FullyQualifiedName~RendererStrategiesAsync|FullyQualifiedName~VulkanFixedRecordsAsync|FullyQualifiedName~VulkanNormalizedClipSamplesAsync|FullyQualifiedName~VulkanImagesAndGlyphRepresentationsAsync"
dotnet publish tests/Graphics/Lumyte.Graphics.Drawing2D.AotSmoke -c Release -p:PublishAot=true -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true -o artifacts/renderer-aot
artifacts/renderer-aot/Lumyte.Graphics.Drawing2D.AotSmoke
artifacts/renderer-aot/Lumyte.Graphics.Drawing2D.AotSmoke wgpu
```

ICDのpathは実機環境に合わせて指定してください。software GPUの結果をhardware結果へ読み替えないでください。上記NativeAOT実行ファイル名はLinux用です。

## 制約

- computeのstroke誤差の証明は正確な算術のbody・bevel・buttに限定します。square/miter/round先端やGPU float32の全丸め誤差の一般的な0.125画素保証ではありません。大きな局所座標で短い弦が量子化される範囲まで、このmatrixから一般化しません。
- 速度下限を証明できないstrokeやcapacityを超える入力は明示拒否し、精度をclampしたり別方式へ戻したりしません。
- GPU前処理には逐次のstroke展開・prefix scanが残ります。大規模シーンでの性能順位・メモリ帯域・表示FPSは未測定です。
- NativeAOT loaderの実行検証はLinux x64です。Windows/macOS/iOS等の実行・配布条件は未検証です。
- GraphをまたぐGPU atlas/imageの自動再利用は保証しません。完了確認のないGPU資産をresidentへ公開しません。
