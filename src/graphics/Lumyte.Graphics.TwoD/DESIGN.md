# Lumyte.Graphics.TwoD 設計

## 目的

`Lumyte.Graphics.TwoD` は、UI、SVG、文字列、画像を同じ描画順序の中で高速に合成する、GPU 主体の 2D 描画ライブラリとする。

主な要件は次のとおり。

- 矩形、角丸矩形、円、直線、画像など、UI で頻出する図形を専用の高速パスで描く。
- 再利用される任意図形は事前ポリゴン化し、GPU path rasterizer を毎回通さずに描けるようにする。
- SVG のパス、塗り、線、グラデーション、クリップを扱う。
- SDF/MSDF、任意パス、事前ポリゴンの各描画経路を用意し、文字列ライブラリが選んだ経路で glyph を描けるようにする。
- CPU 側で作った描画コマンドをバッファとしてシェーダーへ渡す。
- 頂点バッファとインデックスバッファは使用しない。
- `GpuRenderGraph` に `AddTwoD` 拡張メソッドで処理を追加する。
- 即時モードと保持モードの両方を提供し、保持モードでは変更範囲だけを GPU へ転送する。
- DirectX 12、Vulkan、WebGPU で同じ公開 API とシェーダー ABI を使う。

レイアウト、入力、フォーカス、アクセシビリティ、文字列のシェーピング、glyph ごとの描画経路選択は本ライブラリの責務にしない。本ライブラリは、それらの上位層が必要なデータを準備して積み込んだ 2D 表示リストを描画する。

## 設計原則

1. 公開 API では一つの順序付き表示リストを扱う。
2. GPU 上では処理内容に応じて専用経路へ分類する。
3. 半透明要素を描画順序を越えて並べ替えない。
4. GPU データには .NET オブジェクトやバックエンド固有ハンドルを入れない。
5. Render Graph から参照する画像とバッファをすべて宣言できるようにする。
6. 内部色空間は linear、アルファ表現は premultiplied alpha に統一する。
7. GPU ABI は明示的にバージョン管理し、16 byte 境界に揃える。

## 全体構成

```text
 UI / SVG --------------------------+
                                     |
 Text library                        |
 shape -> route selection -> data ---+
                                     v
 CommandEncoder -----> DisplayList (immutable)
          |                         |
          |                 Scene (retained)
          |                         |
          +-------------> DisplayListCompiler
                                  |
            +------------+------------+------------+
            |            |            |            |
       Primitive     Polygon       Path       Layer/clip
        batches      batches      batches        plan
            |            |            |            |
            +------------+-- AddTwoD --+------------+
                                  |
                         GpuRenderGraph passes
                                  |
                         target color texture
```

CPU から見えるのは一つの表示リストだが、コンパイル後は次の専用経路へ分かれる。

| 経路 | 対象 | 主な処理 |
| --- | --- | --- |
| Analytic Primitive | 矩形、角丸、円、楕円、直線、枠、画像 | 手続き的な quad と fragment shader による解析的 AA |
| Distance Field | SDF/MSDF 画像、glyph、アイコン | atlas を参照する手続き的な quad |
| Cached Polygon | 事前ポリゴン化した SVG、アイコン、font outline、静的図形 | ShaderData buffer から triangle 頂点を取得する直接描画 |
| Vector Path | SVG path、任意パス、複雑な stroke | GPU 曲線展開、bounds、tile binning、coverage 生成 |
| Layer Composite | opacity group、mask、filter、特殊 blend | 一時テクスチャへの描画と順序付き合成 |

単純図形と distance field は `SV_VertexID` と `SV_InstanceID` から quad を生成する。事前ポリゴンは triangle 頂点を通常の `BufferId` から vertex shader が取得する。どちらも頂点・インデックスバッファとして bind せず、`Draw` と shader-data buffer だけを使用する。

## 公開 API

### 即時モード

`CommandEncoder` は描画中の state stack を CPU 上だけで解決し、`Finish` で不変の `DisplayList` を返す。GPU は Save/Restore のような状態変更コマンドを解釈せず、各描画コマンドが解決済みの transform、paint、clip を参照する。

```csharp
using CommandEncoder encoder = renderer.CreateCommandEncoder();

encoder.FillRoundedRectangle(
    new Rect(24, 24, 320, 96),
    new CornerRadius(12),
    Brush.Solid(Color.FromSrgb(0.12f, 0.18f, 0.30f, 1.0f)));

encoder.DrawImage(image, new Rect(40, 40, 48, 48));

// 文字列ライブラリが経路を選択し、SDF、polygon、path の
// いずれかの 2D コマンドを encoder へ追加する。
textRenderer.AppendCommands(encoder, textLayout, new Vector2(104, 68), foreground);

DisplayList displayList = encoder.Finish();

context.AddTwoD(
    "ui",
    renderer,
    displayList,
    target,
    new RenderTargetOptions(
        GpuAttachmentLoadOperation.Load,
        GpuAttachmentStoreOperation.Store));
```

### 保持モード

`Scene` は安定した node ID と世代番号を持つ。transform、paint、clip、content、order の dirty range を別々に追跡し、`Prepare` 時に変更された範囲だけを GPU バッファへ反映する。

```csharp
NodeId panel = scene.CreateNode();
scene.SetContent(panel, SceneContent.RoundedRectangle(bounds, radius, brush));
scene.SetTransform(panel, transform);

SceneSnapshot snapshot = renderer.Prepare(scene);
context.AddTwoD("ui", renderer, snapshot, target, options);
```

公開する中心的な型は次のとおりとし、一ファイル一型を基本とする。

| 型 | 責務 |
| --- | --- |
| `CommandEncoder` | 即時モードの state 管理とコマンド記録 |
| `DisplayList` | 不変の描画コマンドと論理リソース参照 |
| `Scene` | 保持型 scene と dirty 管理 |
| `SceneSnapshot` | 一フレームで使用する scene の不変スナップショット |
| `Renderer` | GPU キャッシュ、永続バッファ、atlas、準備処理の所有 |
| `RenderGraphExtensions` | `AddTwoD` と内部 pass の構築 |
| `RenderTargetOptions` | load/store、viewport、最終色変換 |
| `PathGeometry` / `PathBuilder` | Move、Line、Quadratic、Cubic、Close |
| `PolygonGeometry` | 事前ポリゴン化した不変 triangle stream と境界情報 |
| `GeometryCache` | path と許容誤差ごとのポリゴン LOD cache |
| `Brush` | solid、linear/radial gradient、image brush |
| `StrokeStyle` | width、join、cap、dash、miter limit |
| `ImageId` | 表示リスト内の論理画像 ID |
| `DistanceField` | caller が準備した atlas 領域、距離 range、sample 情報 |
| `DistanceFieldRasterizer` | 任意パスから distance field を生成する汎用 GPU 経路 |
| `SvgDocument` | parse 済み SVG と再利用可能な表示リスト |

名前空間は公開 API を `Lumyte.Graphics.TwoD`、SVG を `Lumyte.Graphics.TwoD.Svg`、内部 GPU 実装を `Lumyte.Graphics.TwoD.Gpu` とする。font、glyph、shaping を表す型は TwoD に置かない。

## コマンド形式

公開側は一つの順序付きリストを持ち、GPU 側は固定長 header と用途別 payload の SoA に変換する。

```csharp
// 32 bytes. 実装時はサイズをテストで固定する。
internal struct GpuDrawCommand
{
    public uint Type;
    public uint Flags;
    public uint TransformIndex;
    public uint ClipIndex;
    public uint PaintIndex;
    public uint PayloadOffset;
    public uint PayloadLength;
    public uint Sequence;
}
```

GPU に渡す主なバッファは次のとおり。

| バッファ | 内容 |
| --- | --- |
| Command | opcode、flags、各 table index、元の描画順 |
| Payload | 図形、path、distance-field span 固有の 16 byte aligned データ |
| Transform | 2D affine transform と必要時の逆行列 |
| Paint | 色、gradient、image brush の記述 |
| Clip | 親 index を持つ clip tree |
| Resource | 論理画像から `TextureId`、`SamplerId` への解決結果 |
| PolygonVertex | 事前ポリゴン化した triangle 頂点と AA edge 情報 |
| Batch | renderer kind、command range、scissor、blend、layer |
| Tile | path rasterizer 用の tile offset/count と command index |

同一の transform、paint、clip は intern して共有する。コマンドは `GpuBufferHandle` や `GpuTextureHandle` を直接保持しない。表示リストにある `ImageId` を `AddTwoD` 時に `GpuRenderGraphTexture` と sampler へ関連付け、Render Graph の read 宣言と実際の `TextureId` 解決を同じ登録情報から作る。

コマンド ABI には magic、version、stride を持つ小さな header を付ける。CPU struct、Slang struct、各バックエンドでサイズと field offset を conformance test する。

## 描画順序とバッチング

`DisplayListCompiler` は表示リストを元の順番のまま、同じ renderer kind と互換 state が連続する最大範囲へ分割する。半透明要素を batch 境界の外へ移動しない。

例として `rect, rect, path, distance-field, distance-field, polygon, rect` は `primitive, path, distance-field, polygon, primitive` の五 batch になる。各 batch は内部で instancing、直接 triangle 描画、または tile 処理を行う。Render Graph 上では target の read/write に加えて内部の `GpuRenderGraphDependency` を接続し、batch の順番を明示する。

将来、交互に異なる種類が大量に並ぶ workload 向けに、stable tile list を読む unified compositor を追加できる。ただし初期実装は、挙動が明確で検証しやすい ordered batch を採用する。

## 単純図形の専用パス

単純図形は tessellation せず、command buffer から instance ごとの bounds と parameter を読む。

- vertex shader は `SV_VertexID` から二枚の triangle を生成する。
- fragment shader は矩形、角丸、円、楕円、線分の signed distance を解析的に評価する。
- derivative または pixel scale から anti-aliasing 幅を決定する。
- solid、gradient、image brush を同じ paint table から選択する。
- scissor 可能な矩形 clip は raster state に落とし、角丸/path clip だけ mask を参照する。
- standard source-over は hardware blend を使う。

shadow は単純な box/rounded-box shadow を解析式で扱い、任意形状の blur shadow は layer/filter 経路へ送る。

## 事前ポリゴン化による高速描画

形状が不変で何度も描かれる SVG、アイコン、文字列ライブラリから渡された font outline、装飾図形には `Cached Polygon` 経路を用意する。曲線を毎フレーム GPU path rasterizer へ渡さず、事前に fill/stroke を triangle stream へ変換して `GeometryCache` に保持する。

```csharp
PolygonGeometry geometry = renderer.GeometryCache.GetOrCreate(
    path,
    new TessellationOptions(ToleranceInPixels: 0.25f));

encoder.DrawGeometry(geometry, transform, brush);
```

この経路でも頂点バッファとインデックスバッファは使用しない。

- tessellator は index 付き mesh ではなく、描画順に展開済みの triangle 頂点列を生成する。
- triangle 頂点列は `GpuBufferUsage.ShaderData` の buffer に格納し、`BufferId` で参照する。
- vertex shader は `SV_VertexID` を使って buffer から位置と edge 情報を取得する。
- `Draw(vertexCount, instanceCount)` により、同じ geometry を異なる transform/paint で再利用する。
- 外周 triangle には coverage edge 情報を持たせ、MSAA の有無だけに依存しない anti-aliasing を行う。
- even-odd/non-zero、hole、stroke join/cap を tessellation 結果に反映する。

画面上の許容誤差を満たすため、cache key には path content hash、fill/stroke、tolerance bucket を含める。拡大率ごとの LOD を複数保持し、既存 LOD の誤差を超える拡大では高精度 LOD を生成するか `Vector Path` 経路へ戻す。単一の固定精度ポリゴンを無制限に拡大しない。

command を作る上位層は次を目安に明示的な経路を選ぶ。TwoD compiler は積まれた command の種類を尊重し、別の経路へ自動変更しない。

| 条件 | 積み込む command |
| --- | --- |
| rect、rounded rect、ellipse など解析式で表せる | 専用 primitive command |
| 静的で再利用回数が多く、既存 LOD が画面誤差を満たす | `DrawGeometry` |
| 動的 path、stroke animation、極端な拡大や変形 | `DrawPath` |
| filter、mask、特殊 blend を伴う | 上記で coverage を作り `Layer Composite` |

SVG importer や文字列ライブラリは独自の policy を持てるが、それは TwoD の `RenderOptions` には含めない。TwoD は経路ごとの描画数、triangle 数、path segment 数を diagnostic counter として返し、上位層の policy 調整に使えるようにする。

## SVG と任意パス

`SvgDocument` は XML を毎フレーム解釈せず、一度 parse して `PathGeometry`、paint、transform、clip の再利用可能なデータへ変換する。

最初の対応範囲は次のとおり。

- path、rect、circle、ellipse、line、polyline、polygon
- group、transform、viewBox
- fill rule、stroke join/cap/miter/dash
- solid、linear gradient、radial gradient
- opacity、clipPath

mask、pattern、filter、SVG text は後続段階で追加する。未対応機能を黙って無視せず、parse diagnostic と feature flag で通知する。

旧実装のように CPU で曲線を固定精度の折れ線へ変換せず、Move/Line/Quadratic/Cubic/Close を保持する。GPU path 経路は概ね次の pass で構成する。

1. `PathCount`: transform と画面誤差から必要 segment 数を数える。
2. `PathScan`: prefix sum で出力 offset を決める。
3. `PathEmitBounds`: segment を ShaderData buffer に展開し、screen bounds を求める。
4. `TileCount/Scan/Write`: tile ごとの正確な list 長を数えて格納する。
5. `PathCoverage`: fill rule または stroke に基づく coverage mask を生成する。
6. `PathComposite`: 元の batch 順で paint と coverage を target へ合成する。

固定長 tile list は使わない。count、scan、write の三段階で必要量だけ確保し、overflow による全 path fallback を避ける。初期実装では 16 x 16 pixel tile を既定値とし、GPU と解像度に応じて変更可能にする。

この経路は SVG だけのものではない。文字列ライブラリが font outline を `PathGeometry` に変換して `DrawPath` を積めば、大きな文字も同じ `Vector Path` 経路で直接描画できる。TwoD は入力 path が glyph 由来かどうかを区別しない。

## Distance Field 経路と文字列ライブラリとの境界

TwoD は SDF/MSDF を表示する経路と、任意パスから distance field を作る汎用 GPU rasterizer を提供する。ただし文字列の描画方式は選択しない。

| TwoD ライブラリの責務 | 文字列ライブラリの責務 |
| --- | --- |
| coverage、SDF、MSDF、RGBA atlas を sample する quad 経路 | shaping、fallback font、bidi、line break |
| `DistanceField` の描画 command と shader ABI | projected glyph size と transform の評価 |
| `PathGeometry` から distance field を生成する GPU pass | Coverage/SDF/MSDF/Polygon/VectorPath の選択 |
| 汎用 atlas page の割当と fence-safe な物理領域管理 | font outline の抽出と glyph 単位の論理 cache |
| `DrawGeometry` と `DrawPath` の実行 | 必要データの生成依頼と 2D command の積み込み |

TwoD が扱う atlas 形式は文字専用にしない。

| Atlas 形式 | 2D 経路での用途例 |
| --- | --- |
| R8 coverage | bitmap mask、小さい hinted glyph |
| R8 SDF | 単色 glyph、icon、任意 shape |
| RGB8 MSDF | 角の鋭い glyph、icon、任意 shape |
| RGBA8 color | color glyph、事前着色画像 |

通常の distance field 描画は instance buffer をシェーダーで読み、手続き的 quad から atlas を sample する。項目ごとの頂点は生成しない。command には atlas 領域、bounds、距離 range、paint を入れ、font face や glyph ID は入れない。

文字列ライブラリ側の `Auto` は、例えば次の判断を行ったうえで対応する 2D command を追加する。

| 状況 | 文字列ライブラリが積む 2D command |
| --- | --- |
| 小さい hinted text | coverage atlas を使う `DrawDistanceField` |
| 通常サイズで均一な拡大縮小 | SDF/MSDF atlas を使う `DrawDistanceField` |
| 大きく、同じ outline を繰り返し使い、適切な LOD が cache 済み | `DrawGeometry` |
| 非常に大きい、極端な非等方変形、正確な outline/stroke | outline を渡す `DrawPath` |
| color glyph | `DrawImage` または glyph の vector layers に対応する command 群 |

これにより TwoD の API や batch compiler は、文字サイズ、font、glyph cache policy に依存しない。文字列ライブラリは同じ glyph outline を SDF 生成要求、事前ポリゴン化、任意パス描画で共有できる。

文字列ライブラリから要求された場合、汎用 GPU distance-field rasterizer は次の処理を行う。

1. caller が用意した `PathGeometry` を upload する。
2. 出力領域内の tile へ edge を binning する。
3. 内外判定と最近傍 edge 距離を計算する。
4. SDF/MSDF の値、gutter、必要な mip を atlas へ書く。
5. `DistanceField` として参照できる atlas region を返す。

font face、glyph ID、variation coordinates、render mode などを含む論理 cache key は文字列ライブラリが所有する。TwoD の物理 atlas allocator は中身の意味を知らず、generation 付き region handle と frame fence だけで安全な再利用を管理する。

最初は R8 SDF と R8 coverage の経路を実装し、MSDF は edge coloring と corner preservation を追加する第二段階とする。

## Clip、Layer、Blend

clip は親 index を持つ木として表現し、各 command は解決済み clip node を一つ参照する。

- axis-aligned rectangle は scissor に変換する。
- rounded rectangle は analytic coverage とする。
- path clip は clip atlas の coverage mask とする。
- 同じ clip subtree の mask は再利用する。

opacity group、mask、blur、standard source-over 以外の blend mode は一時 layer を必要とする。`AddTwoD` は layer の寿命区間から transient texture を Render Graph に作成し、使い終わった layer を alias 可能にする。

## Render Graph 統合

`AddTwoD` の正規 API は `GpuRenderGraphContributionContext` の拡張メソッドとする。`GpuRenderGraph` 直下の overload は contributor を登録する convenience API に留める。

`AddTwoD` は command の種類に応じて不要な pass を作らない。

```text
Prepare / dirty upload
        |
        +-- simple only ------------> PrimitiveRaster
        |
        +-- cached geometry --------> PolygonRaster
        |
        +-- paths --> Count/Scan --> Tile --> Coverage --> Composite
        |
        +-- distance-field request -> DistanceFieldRaster
        |
        +-- layers ------------------> LayerRaster --> Filter --> Composite
```

target、画像、gradient/clip atlas、command buffer、path buffer は Render Graph の resource として read/write を宣言する。renderer が所有する永続 cache は import し、一フレームだけの tile list、coverage、layer は transient resource にする。

## シェーダー規約

TwoD の Slang shader は `Lumyte.Graphics.TwoD` DLL に embedded resource として格納する。プロジェクトの `Shaders/**/*.slang` は既存の offline compiler MSBuild task で全 backend 向けに事前コンパイルし、manifest から entry point を取得する。

shader は共通 bindless table の `TextureId`、`SamplerId`、`BufferId` を使う。root data は 128 byte 以下とし、各 pass では次のような小さな値だけを渡す。

- command、payload、transform、paint、clip、polygon、batch buffer の `BufferId`
- command start/count または batch index
- viewport size と inverse size
- tile grid と atlas page
- ABI version と pass flags

resource 本体の handle や可変長データを root data に入れない。

## Graphics 基盤に必要な追加

現状の `GpuCommandBuffer` と Render Graph だけでは、GPU path/SDF の全工程を効率よく表現できない。TwoD 実装に先立って、または第一段階と並行して次を追加する。

1. compute pipeline でも共通 `GpuResourceTable` と root data を設定できる API。
2. read-write ShaderData buffer と storage texture の graph access 宣言。
3. buffer の部分更新、buffer-to-buffer copy、再利用可能な upload ring。
4. prefix sum 結果を使う indirect dispatch。後から indirect draw も追加する。
5. compute、vertex、pixel 間の正確な resource state と barrier。
6. fence-safe な永続 buffer/atlas の拡張と破棄。

WebGPU では sampled texture と storage texture の binding 制約が異なるため、同じ `TextureId` で無理に同一配列へ混在させない。共通規約に storage view table を追加するか、初期 SDF/path coverage を read-write buffer に置くかを基盤実装時に決定する。公開 TwoD API はこの差を露出しない。

## メモリと差分更新

- `CommandEncoder` は arena と `Span<T>` を使い、通常フレームで command ごとの heap allocation を行わない。
- GPU buffer は容量を等比で増やし、毎フレーム作り直さない。
- 保持 scene は transform、paint、clip、payload、order の dirty range を別管理する。
- 構造変更時だけ batch/order を再構築し、色や transform の変更では対応 table の一部だけを転送する。
- path、polygon LOD、distance-field atlas の生成結果は content hash と generation で cache する。font/glyph 単位の論理 cache は文字列ライブラリが所有する。
- offscreen layer と path coverage は Render Graph の transient allocator で再利用する。

## 旧 Luxel 実装からの扱い

`E:/luxel/src/Graphics/Luxel.Graphics.TwoD` の実装は、次の考え方を引き継ぐ。

- segment、path、transform、style、clip、order を分けた SoA。
- bounds、bin、fine raster の GPU pipeline。
- clip の親参照。
- 保持 scene における transform/style/clip/content/order ごとの dirty 管理。
- atlas と frame fence を考慮した resource 再利用。

次の部分はそのまま移植しない。

- 単純 UI 図形まで一つの汎用 path rasterizer に流す構造。
- すべての quadratic/cubic curve を毎回 CPU で固定精度 polyline にする構造。明示的に cache される事前ポリゴン経路は別物とする。
- tile ごとの固定 capacity と overflow 時の全 path scan。
- image/mask を byte-address buffer として持つ構造。
- Render Graph を経由せず rasterizer が command buffer を直接所有・送信する構造。
- UI tree、sprite、tile map、debug draw を raster core と同じ層に置く構造。

## テスト方針

### Unit test

- command encoder の state 解決、Save/Restore、順序。
- GPU struct の size、alignment、field offset、ABI version。
- path validation、fill rule、stroke dash の正規化。
- SVG parse と未対応 feature diagnostic。
- retained scene の dirty range と generation。
- distance-field atlas allocation、generation handle、fence-safe eviction。

### Backend conformance test

DirectX 12、Vulkan、WebGPU で同じ小さな表示リストを描画し、pixel readback で観測可能な結果を検証する。

- solid/rounded/ellipse の中心、境界、外側。
- alpha の重なりと painter order。
- image の `TextureId` と sampler。
- clip tree。
- even-odd/non-zero path。
- caller が準備した SDF 項目の内側、境界、外側。
- 同じ path の `Cached Polygon` と `Vector Path` が許容誤差内で一致すること。
- polygon vertex が ShaderData buffer から読まれ、vertex/index buffer binding を要求しないこと。
- dirty update 後に変更対象だけが変わること。

文字サイズから SDF、polygon、`Vector Path` を選ぶテストは文字列ライブラリ側に置く。その integration test では、選ばれた 2D command の種類と最終描画結果を検証する。

完全一致が保証できない AA 境界は色差の許容範囲を明示する。CPU reference rasterizer は本番経路ではなく、テストと diagnostic のための小さな実装として用意する。

Golden image E2E は後段で追加し、本体 Git には manifest、期待 hash、許容値だけを置く。画像本体は GitHub Pages または release artifact から version 指定で取得し、通常の unit test は network に依存させない。

## 実装段階

### Phase 1: 高速 UI 基盤

- Graphics の compute binding、部分 upload の不足を補う。
- command encoder、display list、renderer、`AddTwoD` を追加する。
- solid rect、rounded rect、ellipse、line、image を procedural quad で描く。
- `PolygonGeometry` と ShaderData buffer を使う事前ポリゴン描画を追加する。
- scissor と premultiplied source-over を実装する。
- 全 backend の conformance test を追加する。

### Phase 2: 保持 scene と distance field

- `Scene` と dirty range upload。
- R8 coverage/SDF atlas と汎用 distance-field instance 描画。
- `PathGeometry` を入力にする GPU SDF rasterizer と fence-safe atlas allocator。
- 文字列ライブラリが選択した経路の command を積める公開 API。

### Phase 3: SVG と GPU path

- SVG parser と reusable document。
- GPU curve expansion、prefix sum、tile binning、coverage。
- fill、stroke、gradient、path clip。
- SVG/path の再利用回数と画面誤差に基づく polygon LOD cache を追加する。

### Phase 4: 高度な合成

- opacity group、mask、blur、shadow、blend mode。
- MSDF、RGBA atlas、SVG filter の段階的追加。
- unified tile compositor と GPU-driven indirect batching の評価。

## プロジェクト依存関係

runtime の基本依存は `Lumyte.Graphics.TwoD -> Lumyte.Graphics` とする。TwoD 固有の `AddTwoD` は `Lumyte.Graphics.Library` ではなく TwoD に置く。共通の blit/composite phase を再利用する場合だけ `Lumyte.Graphics.Library` を参照する。

`Lumyte.Graphics.Shader.Offline` は build-time のみ参照し、runtime dependency にしない。shader source、compiled artifact、manifest は TwoD DLL に埋め込む。
