# Lumyte.Graphics.Drawing2D

通常のCanvas2DとRenderer2DはRenderGraph・ImageEffectsへ依存しません。
図形、パス、画像、外部文字の描画形式、塗りとclipをCPUに記録し、Graphics command bufferへ直接描画します。
レイヤーと画像エフェクトは別パッケージのDrawing2D.RenderGraphに分離しています。

色の公開APIは `Lumyte.Mathematics.Colors.SRgba<float>` を使います。SRgbaは有限なencoded raw値を保存し、負値・HDR・範囲外alphaも許します。描画のcanvas、paint、stop、solid node、target clearは受入時にRGBA全成分を0〜1として検証し、不正値を状態へ保存する前にArgumentOutOfRangeExceptionで拒否します。stopのoffsetは有限値で、重複と範囲外を許し、factoryが非減少順を検証します。defaultは透明黒、透明な非黒のRGBも保持します。パレットの `SRgba8` は `SRgba<float>` 引数へ暗黙に byte /255f で正規化され、gamma変換・premultiplyはしません。一般の `SRgba<T>` はraw値のままです。rawコードのcastは `SRgba8.Red.ToCodes().ConvertChecked<float>()`、明示した結果型への正規化は `ToNormalized<double>()` を使います。

名前付き色はC# 14のstatic extension propertyです。encoded描画には `SRgba<float>.Red`、linear値には `LRgba<float>.Red`、byteコードには `SRgba8.Red` を選びます。getterはofflineで焼き込んだreadonly値のcopyだけで、lookup時の変換はありません。CLR初期化とcopyはliteral constではありません。`CssColors.All/TryGet` は149件のreference catalogとして残ります。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;

var canvas = new Canvas2D();
canvas.Begin();
canvas.FillRect(new Rect2D(20, 20, 100, 50), SRgba<float>.Red);
DrawList2D list = canvas.End();
PaintGraph2D paint = PaintGraph2D.Solid(SRgba<float>.CornflowerBlue);
SRgba<float> color = SRgba<float>.Red; // 既にtarget型の正規化encoded値。

using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new RenderTarget2D
{
    View = targetView,
    LoadOp = AttachmentLoadOp.Clear,
});
using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
{
    prepared.Record(commands);
    commands.Finish();
    using IGraphicsSubmission submission = device.Queue.Submit([commands]);
    await submission.WaitAsync();
}
```

targetViewは同一deviceの単一mip/layerのRgba8SrgbまたはRgba16Float attachmentです。
初期target状態はColorAttachment、入力画像はSampledへ可視化済みとします。
PrepareAsyncはgraphを作らず、固定資源とbatchを準備します。Recordは一回だけ使い、GPU完了後に
command buffer、準備ownerの順に解放します。

既定shaderの単色FillRectは、連続する最大1,024矩形をinstance描画へまとめます。
回転・拡大・DPI・最大8段の矩形clipと、元の4×4 coverage・描画順を維持します。
任意path clip、他のpaint、外部shaderは汎用の評価経路を使います。
upload/data bufferとargument tableはRenderer単位で、準備ownerがGPU完了後に解放されてから再利用します。
idleは最大3組・8 MiBです。未完了の準備間で共有せず、device lossや失敗時は破棄します。

11描画命令には専用interfaceがあり、DrawingCommandRenderers2Dへ実装を直接渡します。
CreateDefault()はdeviceやDIを使わず既定構成を作ります。UseGraphics2Dによる登録と後勝ち上書きも可能です。
外部batchはRecordでupload・compute・render等の複数コマンドを発行し、内部同期を記録できます。
DeclareResourcesでは外部共有texture・bufferのアクセスを宣言し、graph側のadapterが依存へ変換します。
batch私有資源はbatchが所有・同期し、外部資源の入口・出口のstateと可視性を維持します。

塗りは単色、linear/radial/sweep gradient、画像pattern、path clip、変換、隔離合成を扱います。
`PaintGraph2D.Composite(source, backdrop, mode)`はCOLR v1の全28種類の演算を提供します。
sourceとbackdropを同じsubpixel位置で評価して合成し、描画先attachmentの内容は読みません。
色stopの補間とblendはlinear-lightのpremultiplied RGBAです。

COLRの退化したgradientも表現できます。空のstop列は透明、1 stopは一定色です。
3点linearの投影が退化する場合と、同一の2円radialは透明になります。
2円radialの半径は符号付きfinite値を保持し、描画点では補間半径が正の解だけを使います。
簡略形の単一中心radialのradiusは引き続き正数を要求します。
Repeat/Reflectで2 stop以上のoffset spanが0なら透明です。
開始角と終了角が同じsweepは、Padではその角の前後で端色を切り替え、Repeat/Reflectでは透明になります。
`PaintGraph2D.DistanceField`はMSDF/MTSDFで任意のpaintをmaskします。
sourceはsRGBでないRgba8UnormまたはRgba16Floatのatlasとし、0.5を輪郭、値の増加側を内側とします。
DistanceRangeはsource texelで表す距離の総幅です。MSDFはRGB median、MTSDFは拡大時にRGB median、
縮小時にalphaのtrue distanceも使います。sourceのalphaは通常の透明度ではありません。
画像patternのsamplingはraw channel・linear filter・透明な領域外へ正規化されます。
atlasをSampledへ可視化し、GPU完了まで保持する契約は通常の画像patternと同じです。
FillPathはNonZero/EvenOddの複合輪郭を塗り、開いたcontourは塗り時に閉じます。
文字描画には別パッケージの[Lumyte.Graphics.Text](../Lumyte.Graphics.Text/README.md)を使います。
TextLayout2Dをcanvas.DrawTextで記録すると、文字輪郭と装飾をFillPathへ展開し、
同じpaint・変換・clipを適用できます。COLR v1のTextPaintGeometry2Dは、そのpaint graphとboundsを
FillRectへ展開して2Dバックエンドで直接評価します。カラーglyph用のbitmapやtexture uploadは不要です。
MSDF/MTSDFを選ぶ場合はTextパッケージのDistanceFieldText2Dで通常輪郭のatlasを準備し、
uploadを記録してからDrawで矩形へ展開します。混在するCOLR paintはvector graphを維持します。
基本Drawing2Dへフォントライブラリの依存は加わりません。

文字組みは外部ライブラリが担当し、パス・mesh・coverage・SDF/MSDF/MTSDF・画像で描画を依頼できます。
既存のsampler付き`DrawDistanceField`も引き続き利用できます。
Textのatlasでは、任意のpaintと組み合わせられる`PaintGraph2D.DistanceField`を使います。
設計・APIは以下を参照してください。

- [基本描画・paint・資源管理](../../../docs/adr/graphics/GRAPHICS-0018-2d-graphics-engine.md)
- [線・パス](../../../docs/adr/graphics/GRAPHICS-0019-2d-paths-and-strokes.md)
- [画像・mesh](../../../docs/adr/graphics/GRAPHICS-0020-2d-images-and-meshes.md)
- [文字向け形式・COLRv1](../../../docs/adr/graphics/GRAPHICS-0021-2d-glyph-representations.md)
- [Parleyによる文字組み・COLR対応の拡張](../../../docs/adr/graphics/GRAPHICS-0024-text-rendering.md)

## 外部の描画資源

ResourceManagerなしでは、生成済みのpipelineと任意のsamplerをまとめて渡します。

```csharp
using var vertex = device.CreateShader(vertexArtifact);
using var fragment = device.CreateShader(fragmentArtifact);
using var pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
var resources = new DrawingResources2D(device, pipeline);
using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), resources);
using var prepared = await renderer.PrepareAsync(drawList, target);
// Record/SubmitとGPU完了の後に、上記所有者を逆順に解放する。
```

DrawingResources2DはDevice/Pipeline/Samplerの借用参照で、fragment artifactはpipelineのshaderから取得します。
resourcesを省略するとDrawingShaders2Dの埋め込みartifactから必要時に生成し、renderer内で共有します。
既定artifactはDrawingShaders2D.Vertex/Fragmentからも参照できます。

外部資源の所有者をrendererへ移譲する場合はresourceOwnerへIDisposableを渡します。
rendererとpreparedは内部でその所有者を共有し、最後の使用が返った時に一度だけ解放します。
validateResourcesにはResourceLease.Value等の有効性検査を接続できます。検査失敗時に埋め込みへ切り替えません。

Resources連携はLumyte.Resources.Graphics.Drawing2Dを使います。

既定fragment shaderは命令数に応じて8・16・256要素の作業配列を使い分けます。単純なshape・paint・clipで256要素を各coverageサンプルに初期化する負荷を避け、既存の256命令上限と4×4サンプルのcoverage評価を維持します。8/9・16/17・256命令はGPU画素テストで検証します。
AddGraphics2Dで登録し、AcquireRenderer2DAsync→CreateRendererで生成します。
元のResourceLeaseを所有者として一つだけ保持し、描画用のproviderや別のpipeline leaseは作りません。

`DrawingCanvas2D.Replay(DrawList2D)`で保持した描画commandを現在のtransform/clipの下へ再生できます。元のDrawListを変更せず、Widget等の上位構築やOnRecordの再実行を要求しません。描画commandが参照するtexture/font等の借用資源は通常どおりGPU完了まで保持してください。

## 内容と配置を保持する

`Canvas2D.End()` が返す不変の `DrawList2D` を、そのまま `DrawingScene2D` に配置します。別の内容型への変換は不要です。同じ記録を複数の配置・scene で共有でき、位置の変更では Canvas 全体の再記録は不要です。`DrawList2D.Count` は記録済み命令数です。従来の `PrepareAsync(DrawList2D, ...)` も引き続き使用できます。

正規化済みの CPU ページは `DrawList2D` 内で遅延生成・cache し、配置や scene をまたいで共有します。ページ列・容量・ページ数は公開しません。ページ／範囲の公開編集 API はなく、内部の `WithPage` はテスト・計測用の実装です。内容を変更するときは Canvas で新しい `DrawList2D` を記録し、`scene.ReplaceContent(item, replacement)` で置換してください。

```csharp
var canvas = new Canvas2D();
canvas.Begin();
canvas.FillRect(new Rect2D(0, 0, 100, 50), new SRgba<float>(1, 0, 0, 1));
DrawList2D content = canvas.End();

using var scene = new DrawingScene2D();
DrawingNodeId2D group = scene.AddGroup(scene.Root, 0);
DrawingNodeId2D item = scene.Insert(group, 0, content);
scene.SetTransform(group, Matrix3x2.CreateTranslation(20, 10));

PreparedDrawingFrame2D frame;
using (DrawingSceneReadLease2D read = scene.Borrow())
{
    frame = await renderer.PrepareAsync(read, target, cancellationToken);
}
// read の解放後は scene を編集可能。frame の GPU 資源は独立して保護される。
frame.Record(commands);
commands.Finish();
IGraphicsSubmission submitted = device.Queue.Submit([commands]);
frame.AttachSubmission(submitted);
await submitted.WaitAsync(cancellationToken);
commands.Dispose();
frame.Retire();
submitted.Dispose();
frame.Dispose();
```

device、renderer、target、commands の初期化と target の ColorAttachment 状態遷移は省略しています。内容が変われば `ReplaceContent`、親子関係・順序が変われば `Move` を使用します。無効な ID、循環、無効な属性は拒否し、失敗した操作で直前の状態を変更しません。

- 借用中は scene の全編集を拒否します。非同期 Prepare が返るまで借用を解放しないでください。借用解放後の古い CPU メモリーは使用できません。
- 過去の配置が必要な場合だけ `CaptureSnapshot()` を呼びます。通常の編集に、毎回の過去 snapshot 保持を要求しません。
- 木の節は自身の内容を先に描き、次に子を順序通り描きます。自身の描画と子を交互に置く場合は、それぞれを内容を持つ子として配置してください。
- CPU ページと GPU batch は別です。同じ内容ノード内の隣接する互換命令を既存の batch 条件でまとめ、標準矩形の fast path を維持します。ノードをまたいだ batch 統合は行いません。小さなノードへの分割は局所置換を小さくしますが、draw 数が増える場合があります。大きな記録を新しく作って ReplaceContent すると、その内容のページ参照はすべて変わります。group transform/clip の変更では影響する資産を再準備する場合があり、GPU の O(1) 属性更新を保証しません。
- renderer の同時 frame と未使用 cache は `DrawingRetentionOptions2D` で制限します。`RetentionStatistics` は renderer が発行した compile/reuse と未使用資産の概算量で、driver 全体の draw/upload/VRAM 計測ではありません。
- 独自 renderer は既存の登録表を通ります。標準 renderer のみが資産再利用へ参加し、独自 renderer の準備呼出しを推測で省略しません。
- texture/view は借用です。内容の再利用を終え、全 GPU 使用が完了するまで生存させてください。

`Retire` は実際の送信結果が Completed、コマンドが Disposed、device が Ready のときだけ資産を返却します。記録しただけでは転送済みになりません。送信を試していないことが確実な場合は、コマンドを破棄して `AbortUnsubmitted()` を使用します。送信の成否が不明、待機を取り消した、device を失った場合は frame を保持し、GPU が完了したものとして破棄・再利用しないでください。未記録の frame は Dispose で返却できます。

返却済み状態は資産ではなく frame ごとの使用権に保持します。cleanup が失敗して古い Retire を再試行しても、後の frame に貸し出した資産を返却し直しません。GPU 完了または未送信が確定した枠の返却と、失敗した資産 cleanup の再試行を分け、失敗した資産は cache 側が保持します。Prepare が返る前に失敗した場合も枠を失わず、次の Prepare または renderer.Dispose で保留 cleanup を再試行します。cleanup が継続して失敗する場合は新規準備より先にその失敗を通知し、使用中 frame と誤って報告しません。Prepare と cleanup の両方が失敗した場合は、両方の例外を AggregateException に保持します。

この直接描画経路は既存 direct renderer と同様に、RenderGraph の layer/effect 命令を受け付けません。対応しない命令は明示的に失敗し、別 shader へ黙って置き換えません。
