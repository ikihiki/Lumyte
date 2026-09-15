# 2D の SkiaSharp 比較

`TwoDRenderConsumer` が共通 RenderGraph API だけで構築した plan を、DirectX 12／Vulkan の Native runtime と WebGPU の Portable runtime で実行する。`SkiaTwoDReference` は同じ immutable scene の意味を SkiaSharp の path、stroke、gradient、image、layer API で独立に描画する。GPU provider の tessellation、stroke 展開、shader packet を比較画像の生成には使用しない。SkiaSharp はこの非配布の試験用 project だけが参照する。

比較する画素は線形 premultiplied RGBA。入力画像はフィルター処理前に線形 premultiplied へ変換し、gradient は premultiply 済み stop の補間にそろえる。RGBA16Float の HDR 比較は Skia の F16 surface を使い、中間 layer も F16 を維持する。

Skia は各軸 8 倍の解像度で AA 描画し、線形 premultiplied 値を 8×8 の box 平均で出力画素へ戻す。直接の 1 倍描画では線形 surface の境界 coverage に幾何面積からの偏りが観測されたためである。例えば `shapes` の ellipse の画素 `(32,52)` は解析的な面積積分が `0.50596`、直接 Skia が `0.37647`、8 倍 Skia が `0.49020` となった。rounded rectangle の `(56,15)` も真の alpha `0.38720` に対し直接 `0.25098`、8 倍 `0.37647` となる。GPU と一致させる位置ずらしや許容誤差の拡大は行わない。入力画像の補間、blur と距離場の評価もこの参照解像度を踏まえて同じ意味を比較する。

SkiaSharp の標準 gradient は straight-alpha 補間のため、半透明 stop では二つの不透明な Skia gradient で premultiplied RGB と alpha をそれぞれ補間する。小さな `SKRuntimeEffect` はこの二つの出力を RGBA へ組み立てるだけで、座標、gradient 補間、path の分割や coverage を実装しない。layer は親 clip を引き継がない別 surface へ描き、blur、mask、opacity、shadow を評価した後で親 clip 内へ合成する。

画像と gradient は出力画素中心で一度評価する契約なので、別の小さな `SKRuntimeEffect` で評価座標を物理画素中心に固定してから、Skia の子 shader を呼び出す。高解像度化は幾何 coverage に適用し、画像に追加の box filter を掛けない。画像の補間や gradient 関数そのものは Skia が計算する。

使用している Skia raster blur では F16 surface でも HDR RGB が 1 へ切り詰められることを確認した。例えば straight RGBA `(4,2,0.5,0.75)` に opacity `0.6` を適用した中心は、blur 無しで約 `(1.799,0.900,0.225,0.450)`、直接 blur すると約 `(0.600,0.600,0.226,0.449)` となった。そのため blur 前に画像の RGB だけを最大 straight RGB で正規化し、Skia Gaussian を適用後に F16 上で元の倍率を戻す。Gaussian の線形性に基づく変換で、alpha と kernel は変更しない。補正後の中心は約 `(1.795,0.902,0.226,0.449)` となる。shadow は元画像の alpha から色を作って同じ処理に通す。

形状の内部は各 channel `4/255`、AA 境界は `32/255`、全画素の channel 平均絶対誤差は `2/255` 以下を要求する。境界判定は参照画像の 3×3 近傍で行う。画像は位置をずらして合わせず、同じ座標で比較する。失敗時には各 test assembly の `bin/.../TestResults/TwoD/<case>/` に実描画と Skia 参照の PNG を保存し、座標、期待値、実測値、最大・平均誤差を例外へ出す。HDR の判定は元の float 値で行い、診断用 PNG だけを 0–1 に切り詰める。

対象には図形、Bezier path、fill rule、clip と affine transform、stroke の join／cap／dash、各 gradient、画像、prepared text／color glyph、coverage／SDF／MSDF、layer opacity／mask／blur／shadow、Porter–Duff／blend mode を含む。別 pass の出力を 2D の画像入力にするケース、部分更新した retained scene の旧／新 snapshot を複数提出するケースも含む。画像・glyph のデコードや shaping は試験対象に含めず、固定の upload data を渡す。

`post-close-path` は `Close()` の後の `LineTo()` が、閉じた輪郭の始点から別の輪郭を開始する回帰を検証する。fixture は始点 `(32,32)` を共有する非重複の二つの三角形とし、一つ目は `(56,32) → (56,56)`、二つ目は `(8,32) → (8,8)` へ進む。同じ巻方向、水平・垂直・45 度の辺を使い、誤って前の点列を保持すると余分な連結辺ができる。正しい面積は合計 `576`、誤った単一輪郭は `864` となり、例えば画素中心 `(24.5,36.5)` は正しくは透明だが旧処理では塗られる。

初期の fixture は逆向きに重なる三角形を使っており、狭い wedge の AA 画素 `(12,12)` を 3×3 の判定が内部として分類した。そこで alpha `0.68627` と `0.66667` の差が内部用の `4/255` を超えた。これは閉じた輪郭からの再開という回帰とは別の境界分類に依存するため、上記の非重複形状に変更して対象のバグを分離した。共通の内部・境界・平均許容値は変更していない。

この fixture で Portable の輪郭終了処理だけを修正前に戻した実機試験も行い、WebGPU が余計な塗りを検出して失敗することを確認した。最大誤差は `0.74902`、平均誤差は `0.02779` で、例えば `(10,32)` の参照 alpha `0` に対して実測は `0.74902` となった。試験後は修正済みの処理へ戻している。

実機試験は `Category=TwoDConformance` で絞り込める。DirectX 12 と Vulkan は既存の validation fixture を使用する。起動条件・compiler・browser の設定は各 backend の integration test と共通である。

```powershell
dotnet test src/graphics/Lumyte.Graphics.DirectX12.Tests --filter Category=TwoDConformance
dotnet test src/graphics/Lumyte.Graphics.Vulkan.Tests --filter Category=TwoDConformance
dotnet test src/graphics/Lumyte.Graphics.WebGPU.Tests --filter Category=TwoDConformance
```
