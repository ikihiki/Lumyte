# ADR 0036: 2D 描画と文字描画の機能 pass

## 状態

採用（目標設計）。旧 TwoD／Text の調査で確認した描画能力を共通の CPU scene と `Add2DPass` で定義し、Native／Portable の二つの本体で実装する。旧実装は削除済みであり、同等にする範囲と追加提案を区別する。新構成の実装完了を示さない。

## 依存 ADR

| ADR | 利用する契約 |
| --- | --- |
| [0029 Resource Management](0029-resource-management-api.md) | 系統別の資源、descriptor／binding、使用保持と回収 |
| [0030 RenderGraph](0030-render-graph-api.md) | 不変 request、型付き入力 slot、再利用可能な bindings／plan、論理画像、外部依存、非同期提出 |
| [0031 Native RenderGraph](0031-native-render-graph-implementation.md)・[0032 Portable RenderGraph](0032-portable-render-graph-implementation.md) | 本体の登録、受領済み shader／upload data の GPU 準備、内部 graph の構築 |
| [0033 機能 RenderPass](0033-feature-render-passes.md) | 共通の追加 API と二系統の本体の分離 |
| [0034 RenderPass カテゴリ](0034-render-pass-categories.md) | 初期化、画像処理、合成・出力と 2D の分担 |

## 決定

`Add2DPass` は、図形、画像、文字と layer を描画順に重ねる一つの機能とする。利用者は GPU backend を持つ renderer を作らず、CPU 上で scene を組み立てて描画先を渡す。shader、glyph atlas、path の描画方式、GPU buffer、pipeline と descriptor／binding は二系統の本体が所有する。

共通 scene は描きたい内容と合成の意味を表す。Native と Portable が同じ GPU command 列や構造体を解釈するための IR にはしない。CPU の path、画像参照、glyph の配置などは共有できるが、その tessellation、coverage、atlas packing、batch と upload の実装は独立させる。

毎 frame の利用経路は保持 scene を基本にする。一度組み立てた図形・画像・文字を共有し、変更 node だけを編集した不変 snapshot を入力 slot に渡す。同じ外部依存であれば、共通 graph の組立てと Compile は一度でよい。変わらない scene と bindings はそのまま再提出できる。`Draw2DSceneBuilder` は初期内容、独立した小さな content、または毎回作り直すことが適切な描画の入口として残す。

ファイル、URI、asset ID からの画像／font 読込み、画像 decode、font variation／palette の解決、shaping／text layout と SVG の変換は `Lumyte.Resources` の分野とする。この ADR はそれらのロード API を定義せず、準備済みの不変データを GPU へ渡す型と描画 API を定義する。pass は font ID から glyph を探したり、文字列を再 shaping したりしない。

旧ライブラリとの同等性は、削除前の実装とテストを調査した下表を基準にする。当時の設計にしかなかった項目を実装済みとは数えない。旧 API の互換 facade は設けず、GPU の所有と描画経路の選択は本体が担当する。

## 既存機能の対応と追加提案

次の「保持」は新構成でも実装すべき描画能力を意味する。旧コードの保持ではない。表の調査元は削除前の commit `af01785e` であり、新 API と二系統の GPU 実装は未実装である。

| 分類 | 削除前の API／実装で確認した範囲 | 新 API／本体への対応 | 扱い |
| --- | --- | --- | --- |
| 即時描画 | `CommandEncoder`、`Finish()`、不変 `DisplayList` | `Draw2DSceneBuilder`、`Finish()`、不変 `Draw2DScene`。backend を渡さず作成する。 | 保持 |
| 単純図形 | `FillRectangle`、`FillRoundedRectangle`、`FillEllipse`、`DrawLine` | 同名の CPU scene 操作。解析的 AA など実描画経路は本体が選ぶ。 | 保持 |
| path／polygon | `PathBuilder` の Move／Line／Quadratic／Cubic／Close、`DrawPath`、`StrokePath`、`DrawGeometry`、`PolygonGeometry.FromConvexPolygon` | 不変の path／triangle geometry、NonZero／EvenOdd fill。再利用できる geometry の GPU cache は本体が持つ。 | 保持 |
| stroke | 基本 stroke は描画済み。`StrokeStyle` は join／cap／miter／dash を表すが、現行 compiler は dash を拒否し、join／cap／miter の値を GPU 入力へ渡していない。 | Miter／Bevel／Round join、Butt／Square／Round cap、miter limit、dash／offset を実描画まで完成させる。 | 基本を保持、部分実装を補完 |
| paint | `Brush.Solid`、Linear／Radial／Sweep gradient、複数 stop、Pad／Repeat／Reflect。radial は二つの円を指定できる。拡張 gradient の実描画は path 経路に限られる。 | 同名の CPU paint。全 stop と extend を保持し、図形や glyph の種類を理由に描けない組合せを解消する。 | 基本を保持、部分実装を補完 |
| 画像 | `RegisterImage` と `DrawImage`、source rectangle、destination、tint | `Draw2DImageSource` に decode 済みの画像 upload data または logical texture を渡す。実 texture、sampler、登録解除を利用側から除く。 | 保持 |
| state／clip | Save／Restore、scope、affine transform、入れ子 clip。path／回転矩形 clip を伴う通常描画は現行では path 経路に限られる。 | `BeginState`、`SetTransform`／`Transform`、`BeginClip`。画像や単純図形も同じ clip の意味を使えるよう完成させる。 | 基本を保持、部分実装を補完 |
| layer | `LayerOptions` の opacity、mask、blur、shadow、isolated composite | `BeginLayer` と共通の layer options。中間画像と合成 pass は本体が展開する。 | 保持 |
| 合成 | `CompositeMode` の Porter-Duff、Plus、separable／HSL blend。空 layer と clip の組合せもテストがある。 | 一つの `CompositeMode` で意味を定義。互換用の `BlendMode` との二重指定を廃止する。 | 保持 |
| 保持 scene | `Scene` の node ID／世代、content、transform、矩形 clip、visible、order、変更 node の差分 upload | `Draw2DSceneStore` の変更 page と部分木だけを更新して不変 scene を取得する。入力 slot で同一 plan を再利用し、GPU 差分更新は二系統で行う。 | 能力を保持し、CPU snapshot と plan の再利用を拡張 |
| distance field | Coverage／SDF／MSDF の描画と生成、completion 後の atlas 領域回収 | glyph／path 描画の内部経路へ移す。既に生成済みの距離場は画素と距離の規約を持つ `DistanceFieldUploadData` で渡す。 | 能力を保持し管理を内部化 |
| font と shaping | `FontFace` の TTF／OTF／TTC、face index、variation、HarfBuzz shaping、UTF-16 cluster、advance／offset、ascent／descent、outline | 読込みと shaping／layout は `Lumyte.Resources` の分野に分離する。描画側は配置済み glyph、metrics と抽出済み輪郭／paint を受け取る。 | 描画に必要なデータと能力を保持 |
| glyph 描画 | `TextRenderer` の Coverage／SDF／MSDF／Polygon／VectorPath、scale を考慮する Auto | 配置済み glyph と表示品質の契約を受け、各本体が自分の経路を選ぶ。 | 描画能力を保持し経路選択を内部化 |
| color glyph | COLRv0、COLRv1 paint、CPAL palette、foreground、CBDT／sbix の PNG、variation、壊れた color 表現の単色 fallback | palette、variation、color／monochrome と壊れた表現の fallback を解決した paint tree／画素を受け取る。COLR foreground は描画時の brush を参照できる。 | 保持 |
| 段落 text | 現行公開 API は単一 face の shaping と run の描画。font fallback chain、混在 bidi の段落 layout、折返し等の統合 API はない。 | `Lumyte.Resources` の分野で準備した複数 run と配置／metrics を受け取れるデータ型にする。段落処理のサービス API はここで定義しない。 | 描画入力を整備、CPU 処理は別分野 |
| 画像・paint の拡張 | 現行 `Brush` に image brush はない。九分割描画の専用 API もない。 | image brush／tile、nine-slice を scene の便利機能として追加する。 | 追加提案 |
| SVG | 現行設計文書には importer があるが、該当する実装 package はない。 | `Lumyte.Resources` の分野で変換した path、paint、画像と scene を受け取る。SVG importer の API はここで定義しない。 | 入力表現を利用、ロードは別分野 |

調査元は commit `af01785e` の TwoD の CommandEncoder／Brush／PathBatchCompiler／Renderer／Scene と適合試験、Text の FontFace／TextRenderer と適合試験である。必要な場合は Git 履歴から確認する。新しい実装は本 ADR の振舞いを検証し、旧 API の復元を必要としない。

公開型に値を指定できることと、描画できることを分ける。stroke の未使用設定、paint と clip の組合せ制限は、理想の 2D 契約に残す制限ではなく完成させる作業とする。段落 layout や SVG decode は `Lumyte.Resources` の分野であり、この ADR の実装完了条件には含めない。これらから渡された内容を描けるデータ表現を保つ。

## API

共通の `Add2DPass`、request／result／contract は `Lumyte.Graphics.Passes` に置く。CPU scene は `Lumyte.Graphics.TwoD`、準備済み glyph／text の upload data は `Lumyte.Graphics.Text`、両者が使う geometry／paint／画像参照の CPU 値は新設 assembly `Lumyte.Graphics.TwoD.Primitives` に置く。Primitives の namespace は `Lumyte.Graphics.TwoD` を維持し、公開 API 名は変えない。

assembly の参照は TwoD scene → Text → TwoD.Primitives とし、TwoD も Primitives を直接参照する。Primitives は Text、TwoD scene、Passes に依存せず、画像 upload 型と logical texture のために共通 RenderGraph を参照できる。glyph の Outline と scene の path は同じ `PathGeometry` を使い、型の複製や循環参照を作らない。三つの CPU assembly は Native／Portable の assembly を参照せず、font ローダーや text layout サービスは持たない。これらは目標 API であり、同じ名前の現行型の所有契約をそのまま引き継ぐものではない。

### Pass の追加

| API | 契約 |
| --- | --- |
| `graph.Add2DPass(name, Draw2DPassRequest)` | 共通 `AddPass` に `Draw2DPassContract.Instance` を登録し、`Draw2DPassResult` を返す。shader や record callback を受け取らない。 |
| `Draw2DPassRequest(Scene, Color, ReadTextures = [])` | Scene は `GpuGraphValue<Draw2DScene>`。定数 scene または型付き入力 slot を受ける。Color は固定した logical target で、既存内容へ合成するため ReadWrite とする。ReadTextures は scene から参照できる別の logical texture の固定 Read 集合。 |
| `Draw2DPassResult(Color)` | 更新後の同じ logical texture。内部の layer、mask、atlas は返さない。 |
| `Draw2DPassContract.Id / Version / Instance` | `lumyte.draw.2d` と version `1` を共通契約とする。 |
| `Snapshot(request)` | 固定 target、固定 ReadTextures と定数／slot の指定を固定する。定数 scene の論理画像集合は一度だけ抽出して ReadTextures に含められる。slot に後から渡される値を固定 request へコピーしない。 |
| `Declare(context, request)` | target の ReadWrite と ReadTextures の Read を宣言し、`ReadInput(request.Scene, Draw2DSceneInputContract.Instance)` を登録する。入力更新から外部依存を追加しない。 |
| `Draw2DSceneInputContract.Instance` | `IGpuGraphInputContract<Draw2DScene>` の共有実装。`Snapshot(scene)` は所有済みの不変 scene をそのまま保持し、`Retain(context, scene)` は記録済みの upload data を ReadUpload、論理画像を UseDeclared へ渡す。 |

`GpuGraphValue<Draw2DScene>` は scene 定数と `GpuGraphInput<Draw2DScene>` から暗黙変換できる。入力 slot を使う場合は、そこから利用できる logical texture を graph 構築時の ReadTextures に明示する。各 snapshot はこの集合の一部だけを使ってもよく、集合内で画像を差し替えられる。新しい外部画像を追加するときは依存集合が変わるため新しい plan を作る。準備済み `GpuImageUploadData` の差し替えは本体の内部資源の変更として処理し、この外部画像集合を増やさない。

`Retain` は固定した依存内の値の保持に限る。target 自身を scene の画像として使うこと、新しい logical texture を UseDeclared から追加すること、別 graph の参照を使うことは許可しない。Lumyte が確認するのはこの共通 graph の契約であり、GPU usage 等の native API の検証を複製しない。

2D pass は target 全体の初期化を暗黙に行わない。空の target を使う場合は先行する Clear pass を追加する。旧 `RenderTargetOptions` の GPU load／store は公開しない。最終的に保持する画像は caller が MarkOutput／Export する。

version 1 の target は単一 layer／mip、sample count 1 の 2D texture とし、線形色の RGBA8／BGRA8 UNORM と RGBA16Float を対象にする。multisample の scene を重ねる場合は先に Resolve を行う。layer と backdrop の実装が読む先行色は内部の依存と copy に載せ、caller に Sampled 等の GPU usage 指定を要求しない。

### Scene の組立てと保持

| API | 契約 |
| --- | --- |
| `Draw2DSceneBuilder(deviceScale = 1)` | CPU 上の描画記録を開始する。deviceScale は logical unit から物理 pixel への正の倍率。GPU device を渡さない。 |
| `FillRectangle(rect, brush)`／`FillRoundedRectangle(rect, radius, brush)`／`FillEllipse(bounds, brush)` | 不変の図形を現在の state で追加する。円は同じ幅と高さの ellipse とする。 |
| `DrawLine(start, end, width, brush)` | 単純な線分を追加する。複雑な cap／join／dash が必要なら StrokePath を使う。 |
| `DrawPath(path, brush, fillRule)`／`StrokePath(path, brush, stroke)` | path の fill／stroke を追加する。fillRule の既定は NonZero。 |
| `DrawGeometry(geometry, transform, brush)` | 再利用できる不変の triangle geometry を描く。GPU buffer や pipeline は指定しない。 |
| `DrawImage(source, destination, sourceRectangle = null, tint = null, sampling = default)` | 画像全体または pixel 単位の部分矩形を描く。既定の tint は白、filter は Linear、外側は Clamp。 |
| `DrawDistanceField(data, destination, brush)` | `DistanceFieldUploadData` を描く。画素、Coverage／SDF／MSDF、元の大きさ、distance range を入力に含め、物理 atlas の位置は含めない。 |
| `DrawText(data, origin, brush)` | 配置済みの不変 `TextDrawData` を追加する。origin は text bounds の左上とし、glyph の位置と line baseline は data が持つ。font や文字列からの再 shaping は行わない。 |
| `BeginState()`／`BeginClip(rect)`／`BeginClip(path, fillRule)`／`BeginLayer(options)` | LIFO の `Draw2DScope` を返す。Dispose で対象の state／clip／layer を閉じる。 |
| `SetTransform(matrix)`／`Transform(matrix)` | affine transform を置換／合成する。scope を閉じると親の状態に戻る。 |
| `Finish()` | scope が閉じた記録を不変の `Draw2DScene` として返す。GPU 準備・転送は行わない。 |
| `Draw2DSceneBuilder.Dispose()` | 未完了の CPU 記録を破棄する。返却済み scene には影響しない。 |
| `Draw2DScene` | 順序、状態、upload data とその Key、deviceScale を固定した完全な CPU snapshot。store の内容世代と構造共有する page／部分木、記録済みの upload 所有集合と論理画像参照 index を持つ。GPU object を所有しない。 |
| `Draw2DSceneStore.CreateNode(content)`／`Remove(node)` | 保持 scene の node を追加／削除する。content は不変 scene として表し、即時 scene と描画能力を揃えられる。 |
| `SetContent(node, scene)`／`SetTransform(node, matrix)`／`SetClip(node, rect)`／`SetVisible(node, value)`／`SetOrder(node, order)` | node の状態を更新し、CPU の内容世代を進める。null clip は矩形 clip の解除。 |
| `Draw2DSceneStore.Snapshot(deviceScale = 1)` | order、同値なら作成順で合成する不変 scene を返す。変更 page／部分木と参照 index の変更箇所だけを確定し、残りを共有する。変更がなければ同じ snapshot を返せる。過去の snapshot を変更しない。 |
| `Draw2DNodeId` | store と世代に属する ID。削除後の ID が再利用 node を指すことはない。 |

state scope は Save／Restore と同じ能力を持つため、両方の API 群を重複公開しない。過去の snapshot は参照がある限り CPU 上で保持でき、GPU cache の寿命は実行の使用保持で別に管理する。保持 scene の差分は GPU 転送の最適化情報であり、変更部分だけを描けばよいという target の履歴契約ではない。

content 作成時に画像、text、glyph、paint の明示的な子 data を一度だけ集め、所有済みの不変参照集合を content と一緒に保持する。store の編集は変更 content とその祖先の参照集合だけを更新し、snapshot はこの集合も構造共有する。Snapshot、入力 bindings の Set、同じ bindings の再提出で、全 node、全 glyph や全画素の複製・hash 計算・再走査を行う設計にしない。保持情報も不変 root／page 単位で再利用し、未知の object graph を reflection で探索しない。

変更 index は CPU／GPU cache の更新を速める補助情報であり、差分を順番に消費しないと描けない契約にはしない。各 snapshot 単体で内容と所有参照が完結し、frame を飛ばした提出、過去世代の再提出、別 runtime の初回使用、cache 回収後の使用を成立させる。変更履歴が利用できない場合は保持 page の比較または完全な初回準備へ戻れる。全 snapshot の履歴を永久保存する必要はない。

### 描画値、画像、layer

| API | 契約 |
| --- | --- |
| `Rect`／`CornerRadius`／`Color` | logical 座標、角ごとの半径、線形色の非乗算 RGBA。`Color.FromSrgb` は RGB を線形化し、alpha は変更しない。GPU 合成の前に一度 premultiply する。 |
| `PathBuilder.MoveTo/LineTo/QuadraticTo/CubicTo/Close()`／`Build()` | 曲線を保持した不変 `PathGeometry` を作る。CPU で GPU 用の分割数を固定しない。 |
| `PathGeometry`／`PolygonGeometry(vertices)`／`PolygonGeometry.FromConvexPolygon(points)` | 不変の曲線／三角形列／凸 polygon の変換。入力配列はコピーする。 |
| `FillRule`／`StrokeStyle` | NonZero／EvenOdd と、幅・join・cap・miter limit・dash／offset。奇数個の dash は列を二回繰り返して周期を作る。 |
| `Brush.Solid/LinearGradient/RadialGradient/SweepGradient(...)` | 色、制御点／円／角度、不変 GradientStop 列、GradientExtendMode を持つ CPU paint。 |
| `GradientStop(Offset, Color)`／`GradientExtendMode` | 色線の stop と Pad／Repeat／Reflect。等しい offset の stop は入力順を保ち、鋭い境界を表せる。 |
| `Draw2DImageSource.Upload(GpuImageUploadData)` | decode 済みの画素、extent、format、色と alpha の規約を持つ不変 upload data を直接参照する。asset ID の解決や画像 decode は行わない。 |
| `Draw2DImageSource.Texture(texture)` | この graph に属する先行 logical texture を参照する。別 graph でも使用できる CPU upload data とは区別する。 |
| `Draw2DImageSampling(Filter, ExtendX, ExtendY)` | Nearest／Linear と Clamp／Repeat／Mirror の画像上の意味。実 sampler は本体が管理する。 |
| `Draw2DLayerOptions` | `Bounds`、`Opacity = 1`、`CompositeMode = SourceOver`、任意の Mask、`BlurRadius = 0`、任意の Shadow。Bounds 省略時は親 clip と target の交差範囲。 |
| `Draw2DLayerMask(Image, Destination)` | layer の座標へ配置した画像の alpha を coverage とする。画像を読む依存を scene に含める。 |
| `ShadowOptions(Offset, Color, BlurRadius = 0)` | layer の alpha から作る影。offset と blur の大きさは logical unit。 |
| `CompositeMode` | Clear、Source、Destination、SourceOver、DestinationOver、SourceIn、DestinationIn、SourceOut、DestinationOut、SourceAtop、DestinationAtop、Xor、Plus、Screen、Overlay、Darken、Lighten、ColorDodge、ColorBurn、HardLight、SoftLight、Difference、Exclusion、Multiply、HslHue、HslSaturation、HslColor、HslLuminosity。 |

画像 upload data は [ADR 0030](0030-render-graph-api.md) の `GpuImageUploadData` と `GpuImageSubresourceData` を使い、extent、format、mip／layer、row／slice stride と画素を明示する。色の規約は `GpuImageColorEncoding`（Linear／Srgb／Data）、alpha は `GpuImageAlphaMode`（Opaque／Straight／Premultiplied）で表す。logical graph texture は線形 premultiplied RGBA の共通画像を受け付け、二度の decode／premultiply を行わない。同じ target を scene の画像入力として読む契約は設けず、必要なら先行 Blit で別の logical texture に分ける。graph texture を含む scene はその graph に束縛される。

layer は描画内容を透明から作り、layer 空間で blur、mask、opacity を適用して親へ composite する。影は元の layer alpha を指定半径でぼかして offset／色を適用し、親へ SourceOver してから本体を合成する。親 clip は最終合成に適用する。透明な空 layer でも Clear／Source 等は Bounds の結果を変えるので、単に内容が空という理由では削除しない。

layer の blur は有限範囲の Gaussian とする。物理半径を `R = BlurRadius × deviceScale`、標準偏差を `R / 3` とし、各軸の整数 `[-ceil(R), ceil(R)]` における Gaussian 重みを総和 1 に正規化する。範囲外は透明な黒、radius 0 は恒等とし、影にも同じ規約を使う。これは現行の重み付き blur の能力を保ちつつ新しい両本体の意味を固定するもので、旧実装との pixel 互換を要求しない。外部の画像 Blur と kernel／端の扱いが異なるため、共通 AddBlurPass の呼出しを強制せず、各本体の内部 graph で実装する。

### Glyph、文字と距離場の upload data

画像・font の読込みと shaping／layout を済ませた側から、不変の描画データを受け取る。`IGpuUploadData.Key` の `GpuUploadDataKey(Id, Revision)` は内容の識別子であり、font、URI、asset source を再解決するための場所ではない。データは渡された時点で完結し、同じ Key の内容を後から変更しない。

これらは backend に依存しない GPU upload の入力型である。最終的な GPU buffer の byte 配置、glyph atlas の座標や shader 構造体を共通化するものではなく、二系統がそれぞれ変換・配置する。

| API | 契約 |
| --- | --- |
| `TextDrawData(Key, Size, Lines, GlyphRuns) : IGpuUploadData` | 配置済み文字の不変 snapshot。Size は logical unit の全体サイズ。各 run が使う glyph data を直接保持する。元の文字列や font file の再読込みを必要としない。 |
| `TextLineMetricsData(Baseline, Ascent, Descent, AdvanceBounds, InkBounds)` | text の左上を基準にした baseline と正の ascent／descent、論理 advance の範囲と実際の描画範囲。測定済みの値をそのまま公開する。 |
| `TextGlyphRunData(Glyphs)` | 描画順の不変 `PositionedGlyphData` 列。異なる font、fallback、color glyph を同じ text に含められる。font を再取得するための run ではない。 |
| `PositionedGlyphData(Glyph, Transform, Advance, Utf16Cluster)` | `GlyphUploadData` への直接参照、glyph ローカル座標から text の左上基準の logical 座標への変換、測定済み advance と元文字列の cluster。glyph の抽出・サイズ・variation と配置は確定済みとする。 |
| `GlyphUploadData(Key, Bounds, Outline, ColorPaint) : IGpuUploadData` | 不変の glyph 輪郭と、任意の解決済み color paint tree。Bounds は glyph ローカル座標の ink bounds。ColorPaint があればそれを描き、なければ Outline を DrawText の brush で塗る。両方が空なら空白等の描画のない glyph とする。 |
| `GlyphPaintNode` | 有限の不変 paint tree。下表の輪郭、色、gradient、bitmap、transform、clip、合成で color glyph の描画を表現する。font の表 offset、glyph ID だけの未解決参照、palette index は含めない。 |
| `DistanceFieldUploadData(Key, Image, Kind, SourceSize, DistanceRange) : IGpuUploadData` | `Image` は `GpuImageUploadData`。Kind は Coverage／SDF／MSDF、SourceSize は距離場の元の描画サイズ、DistanceRange は距離の符号化範囲。物理 atlas の位置や backend handle を持たない。 |

`GlyphUploadData.Outline` は `PathGeometry` と fill rule の組とし、font の輪郭を不変の曲線として保持する。palette の実色、variation、COLRv0 layer と COLRv1 の glyph 参照は提出前に `Lumyte.Resources` 側で評価・解釈する。color／monochrome の選択、壊れた color 表現からの fallback も供給側で確定する。bitmap glyph は decode 済み画像を ColorPaint の Bitmap node に含める。入力には PNG／TTF／OTF／TTC のファイル byte 列や未解決の font 表を含めない。これにより描画本体は font parser を持たず、既存の色文字と単色文字の能力を維持できる。

| `GlyphPaintNode` の variant | 内容 |
| --- | --- |
| `Outline(Path, FillRule, Paint)` | glyph ローカルの曲線領域に子 paint を適用する。別 glyph の輪郭も抽出済みの Path として渡す。 |
| `Solid(Color)`／`Foreground(Alpha)` | 解決済み線形色、または DrawText の brush を使う foreground。Foreground は font palette の遅延参照ではない。 |
| `LinearGradient`／`RadialGradient`／`SweepGradient` | 制御点／円／角度、extend、不変 stop 列を持つ。stop の色も解決済みの Color または Foreground と alpha で表せる。 |
| `Bitmap(Image, SourceRectangle, Destination)` | decode 済み `GpuImageUploadData` の画素と配置。PNG／font blob を内部で開かない。 |
| `Transform(Matrix, Child)`／`Clip(Path, FillRule, Child)` | affine transform と輪郭による clip。 |
| `Composite(Source, Backdrop, Mode)`／`Layers(Children)` | 共通 CompositeMode に従う合成、または入力順の SourceOver layer 列。 |

SDF は線形 UNORM の R、MSDF は RGB の median を距離値とし、`distance = (value - 0.5) × DistanceRange` を SourceSize の pixel 単位で表す。符号は内側を正とする。Coverage は R の `0..1` を coverage として使い DistanceRange を 0 とする。既存の距離場が異なる符号化を持つ場合は、供給側がこの規約に変換した画素を渡す。距離場を使わず輪郭から描く場合や、本体が自分の atlas 向けに距離場を生成する場合は共通の物理表現を要求しない。

入力配列、paint tree、輪郭と画素はコピーまたは所有権の移譲によって owning immutable snapshot にする。`ReadOnlyMemory` という型だけを理由に外部で変更・回収され得る配列を受け入れない。`Lumyte.Resources` の cache eviction やロード用オブジェクトの Dispose で既存 scene の参照が無効にならないことを受渡し契約にする。scene の作成・変更時に text、glyph、bitmap、距離場と画像の明示的な子 data の所有集合を記録する。入力 contract の Retain はこの記録を使って `context.ReadUpload(data)` へ登録し、不変の保持集合を再利用する。graph による未知の object の探索や、asset source の再読込みは行わない。

glyph の実描画経路と atlas サイズはこれらの入力に含めない。通常サイズから大きな拡大、非等方 transform、color glyph まで描ける能力を保ち、品質を満たす経路を各本体が選ぶ。cache の有無で advance、line break や glyph の配置が変わってはならない。

### 追加提案と責務外の処理

次の描画機能は既存との同等性を越える提案とし、基本の二系統移行の完了条件から分ける。機能を追加するときも GPU API を利用者へ公開しない。

| API 案 | 提案する範囲 |
| --- | --- |
| `Brush.Image(source, transform, sampling)` | image を繰り返す paint。準備済み画像は ReadUpload、logical texture は Read という通常の画像と同じ規約で扱う。 |
| `DrawNineSlice(source, destination, insets, sampling)` | 境界幅を保つ画像描画。CPU の意味だけを追加し、Native／Portable の batch 化を独立させる。 |

font fallback chain、複数 run、折返し、混在 bidi、行間、ellipsis と hit testing は `Lumyte.Resources` の分野で検討する。Graphics はそこから渡された配置と metrics を保持して描く。SVG も同じ分野で path、基本形状、group、transform、fill／stroke、gradient、clip、画像へ変換する。ここでは `LoadFont`、`LayoutAsync`、SVG importer 等のサービス API を新設しない。

日本語の禁則・改行、結合文字、surrogate pair、emoji sequence、RTL／LTR 混在のレイアウトが準備済みなら、その glyph 順・位置を描画側が変更しない。Unicode 処理版、font fallback 順と対応する SVG 要素の選定は供給側の契約とする。縦書き、ruby、文章編集、IME、accessibility tree、widget layout もこの 2D 描画 pass の責務外とする。

## 座標、色と描画品質

原点は target の左上、X は右、Y は下とする。座標と線幅は logical unit で、scene の deviceScale を最終の物理座標変換に一度だけ掛ける。保持 scene の子 snapshot は deviceScale 1 とし、外側の snapshot で出力倍率を指定する。path と画像には描画時の affine transform、clip には clip を追加した時点の transform を使う。

内部の色計算と合成は線形 premultiplied RGBA とする。color glyph palette と通常の画像の色変換もこれに合わせる。Plus は color と alpha を飽和し、その他の composite は現行テストの premultiplied reference を基準にする。sRGB encode、HDR tone mapping と presentation は別の機能で行い、2D pass が最後の出力変換を推測しない。一般的な画面合成では tone mapping 後の線形 SDR に UI を重ね、最後に Output pass へ渡す。

alpha を含む描画順を維持する。同じ resource／paint が遠くに出現しても、間の描画との関係を変える並べ替えは行わない。互換な連続範囲の batch 化や、結果を変えない tile 内の処理は可能とする。clip、opacity group と backdrop を読む blend の境界もこの順序に含める。

AA は Coverage、解析式、path coverage、distance field 等で実現できる。新契約の適合試験では、塗りつぶし内部の色、境界の位置と coverage、文字の metrics を別に評価し、両本体の pixel が常に bit 単位で等しいことは要求しない。誤差の許容値と対象 format は試験 fixture に固定し、境界以外の色や alpha の誤りを広い画像許容差で隠さない。小さい文字と極端な拡大・変形で現行より描画能力を落とさない。

## 二系統の実装と資源の寿命

| 対象 | Native の本体 | Portable の本体 |
| --- | --- | --- |
| 本体 | `NativeDraw2DPass` が `INativeRenderPass<Draw2DPassRequest, Draw2DPassResult>` を実装する。 | `PortableDraw2DPass` が `IPortableRenderPass<Draw2DPassRequest, Draw2DPassResult>` を実装する。 |
| shader と GPU data | host から渡された展開済み Native shader package、その GPU program を作る loader、専用 root／parameter 構造体を所有する。 | host から渡された展開済み Portable shader package、その GPU program を作る loader、専用 root／binding 入力を所有する。 |
| 描画資源 | 統一 heap、linear range、Bindless descriptor を Native Resources で管理する。 | Buffer／Texture を直接生成し、有限の明示 binding を Portable Resources で管理する。heap 作成や Bindless emulation を追加しない。 |
| 最適化 | descriptor による画像切替え、range の集約、独自 batch／path 処理を選ぶ。 | binding 互換性を使う連続 batch、atlas page ごとの分割、独自 raster／compute 処理を選ぶ。 |
| cache | device と upload data の Key に属する geometry、glyph、atlas、pipeline 記述、scene page の変換結果を保持する。 | 同じ内容を対象にしても、独立した物理 cache、binding cache と scene page の変換結果を持つ。 |

両本体の `BuildAsync` は供給済み shader package からの GPU program 作成と、request に含む upload data の GPU 変換・転送を行い、内部の copy／path preparation／coverage／layer／composite pass を登録する。shader artifact の I/O は `Lumyte.Resources` の分野で行い、host が二系統の pass 実装へ展開済み package を渡す。runtime の asset source や shader ファイル探索を本体に追加しない。利用側に `Prepare`、`Renderer`、`TextRenderer`、atlas の Release／Collect、低レベル shader や GPU 構造体を要求しない。新たな独自 2D 効果を pass として提供する作者は、共通契約と二系統の本体を追加する。

実行時には入力 bindings からその実行の scene snapshot を解決する。BuildAsync が各提出で呼ばれても、同じ内容の program 作成、path 変換、glyph 準備や全 scene の batch 構築を繰り返すことを意味しない。各本体は保持 page、content Key と描画状態の変更を用いて、必要な GPU 範囲と内部 pass の差だけを準備する。共通 plan の再利用は物理 command buffer の無条件な再利用を意味せず、GPU command の記録と資源状態の処理は系統ごとに選ぶ。

失効範囲は変更 node 一つに限らない。transform、clip、visible、order の変更は、交差する batch、coverage と描画順に影響する範囲へ伝播する。layer の mask／opacity／blur、親 clip と backdrop を読む合成は、その layer と依存する後続合成まで更新する。glyph／画像 atlas の新しい配置は、その世代を参照する batch の入力と binding を更新する。DeviceScale の変更では scale に依存する tessellation、coverage、glyph 表現、blur の物理半径を失効させ、元の不変輪郭・画素は共有する。全 scene に影響する変更で広い再準備が必要なことと、単一 transform の変更で静的画像まで再転送することを区別する。

内部 atlas に格納した領域は、実行が参照する位置と世代を GPU completion まで維持する。cache eviction や atlas の拡張で提出済み参照先を書き換えず、新しい世代を公開する。slot と領域は最後の GPU 使用後に返す。glyph、画像と geometry の内容 Key、scene の CPU revision を cache key に含め、別 runtime の GPU object を共有しない。

CPU scene と GPU cache の保持は [ADR 0030 の不変入力と使用保持](0030-render-graph-api.md#不変入力と使用保持) に従う。graph 由来の画像は通常の import／export と execution／pin を使う。atlas の座標と内容、そこから作った batch の GPU 入力をそれぞれの内容世代へ結び付ける。

glyph atlas の更新、保持 scene の差分、path data の upload は本体が明示する。提出順序と結果は [ADR 0030 の GPU 内容世代と提出結果](0030-render-graph-api.md#gpu-内容世代と提出結果) に従い、RegisterContent／TryUseContent へ接続する。atlas の転送に依存する batch もその結果依存を受け取り、転送に失敗した領域を描画可能な cache entry として再利用しない。

root data は各系統の直接入力を維持する。大きな scene／glyph／paint data は本体が通常の resource として作り、shader が root から index／offset 等を使って参照する。低レベル command に Parameter Data の自動生成・転送や root の buffer fallback を追加しない。実 raster PSO は実際に提出する組合せについて下位 Submit で解決する。

coverage、gradient、距離・曲線計算、premultiplied 合成は [ADR 0033](0033-feature-render-passes.md) の Slang module として共有できる。各 entry は自身の atlas／glyph／paint の GPU 表現を読み、意味上の値を共有関数へ渡す。共有 source のために atlas 配置、binding や batch の方式を統一せず、Portable は直接 root を満たす toolchain 経路または明示した直接 WGSL authoring を使う。

## コード配置

以下は repository root からの相対パスによる目標配置である。TwoD／Text は CPU 契約として新設し、共有値の TwoD.Primitives、feature の契約、二系統の GPU 本体と Hosting integration も新設予定とする。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Passes/TwoD/` | `Add2DPass`、`Draw2DPassRequest`／result／contract と `Draw2DSceneInputContract`。 |
| `src/graphics/Lumyte.Graphics.TwoD/Scene/` | 新設予定。`Draw2DSceneBuilder`／scope、`Draw2DSceneStore`／node ID／snapshot、描画順と共有 page・所有情報。 |
| `src/graphics/Lumyte.Graphics.TwoD.Primitives/Geometry/`、`src/graphics/Lumyte.Graphics.TwoD.Primitives/Paint/`、`src/graphics/Lumyte.Graphics.TwoD.Primitives/Images/` | 新設 project。path／polygon、stroke、色／brush／gradient、画像参照・sampling、clip／layer／composite の共有 CPU 描画値。namespace は `Lumyte.Graphics.TwoD` とし、Text と scene の両方から参照する。 |
| `src/graphics/Lumyte.Graphics.Text/Upload/` | 新設予定。`TextDrawData`、metrics、配置済み glyph、輪郭と color paint tree、`DistanceFieldUploadData` の所有済み転送契約。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/TwoD/` | `NativeDraw2DPass` と内部 path／coverage／layer／glyph 処理。`GpuData/`、`Upload/`、`Cache/` に専用入力、差分転送、atlas と scene／batch cache を置く。 |
| `src/graphics/Lumyte.Graphics.Portable.Passes/TwoD/` | `PortableDraw2DPass` と自身の内部処理。同じ分類の `GpuData/`、`Upload/`、`Cache/` に明示 binding と atlas page に適した実装を置く。 |
| `src/graphics/Lumyte.Graphics.Native.Passes/TwoD/Shaders/*.slang`、`src/graphics/Lumyte.Graphics.Portable.Passes/TwoD/Shaders/` | Native の Slang entry と Portable の Slang／直接 WGSL entry、図形・path・glyph・mask／layer の専用 resource／root 宣言。生成 C# と artifact は各 project の `obj/<Configuration>/<TargetFramework>/Shaders/Native/` または `Shaders/Portable/` に出力する。 |
| `src/graphics/Shaders/Shared/TwoD/`、`src/graphics/Shaders/Shared/Color/` | coverage、gradient、距離・曲線計算、premultiplied 合成の共有 Slang module。atlas の読取り、binding、batch、AA の実行方式は各本体に置く。 |
| `src/graphics/Lumyte.Graphics.Passes.Hosting/TwoD/` | `Add2DRendering()`、両本体の登録と shader package を渡す起動用定義。 |
| `src/graphics/Lumyte.Graphics.TwoD.Primitives.Tests/Unit/` | 新設の隣接 xUnit project。geometry／paint／画像参照の値と不変性を確認する。 |
| `src/graphics/Lumyte.Graphics.TwoD.Tests/Unit/`、`src/graphics/Lumyte.Graphics.Text.Tests/Unit/` | 新設する隣接 xUnit project。scene 編集、glyph／text の所有と snapshot、共通 primitive の利用を確認する。 |
| `src/graphics/Lumyte.Graphics.Passes.Tests/Unit/TwoD/` | 新設の隣接 xUnit project で pass の固定 I/O、scene 入力と依存宣言を確認する。 |
| `src/graphics/Lumyte.Graphics.Native.Passes.Tests/Unit/TwoD/`、`src/graphics/Lumyte.Graphics.Portable.Passes.Tests/Unit/TwoD/` | 新設の隣接 xUnit project。fake による差分準備、atlas 世代、使用保持と失敗の試験。 |
| `src/graphics/Lumyte.Graphics.Native.Passes.Tests/Integration/TwoD/`、`src/graphics/Lumyte.Graphics.Portable.Passes.Tests/Integration/TwoD/` | GPU を使う図形・文字・色・clip／layer と更新結果の適合試験。既存 GPU fixture を移し、unit suite と分離する。 |
| `benchmarks/Lumyte.Benchmarks/Graphics/TwoD/` | 既存 benchmark project に追加する scene の未変更／部分変更、glyph・画像再利用、転送量と描画負荷の計測。 |

旧 TwoD／Text の GPU renderer、shader と atlas 管理は両系統の本体へ移し、旧 API の互換層を残さない。font ロード／shaping／layout、画像／SVG decode は `Lumyte.Resources` の分野へ分離し、CPU 契約 project に loader を残す理由にしない。TwoD／Text の共有値を Primitives に置くことで相互の project 参照を避ける。Primitives は Text／scene／Passes を参照せず、共通画像 upload 型と logical texture は RenderGraph の定義を参照する。共通 scene と glyph は GPU 本体・Hosting に依存しない。

## 使用例

host が両系統の `Draw2DPassContract` 実装と展開済み shader package を登録済みとする。`color` は先行 pass が初期化した線形 premultiplied RGBA の logical texture。`titleData` は `Lumyte.Resources` 側で配置・glyph 抽出を済ませた `TextDrawData`、`iconData` は decode 済みの `GpuImageUploadData` とする。これらを作るロード API はこの ADR で定義しない。content と graph を一度作り、位置だけを更新する例を示す。

```csharp
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.TwoD;
using Lumyte.Graphics.Text;

using var draw = new Draw2DSceneBuilder();
draw.FillRoundedRectangle(
    new Rect(16, 16, 320, 100), new CornerRadius(12),
    Brush.Solid(Color.FromSrgb(0.12f, 0.15f, 0.20f)));
using (draw.BeginClip(new Rect(24, 24, 304, 84)))
{
    draw.DrawImage(Draw2DImageSource.Upload(iconData), new Rect(32, 36, 32, 32));
    draw.DrawText(titleData, new(80, 40), Brush.Solid(Color.White));
}

var scene = new Draw2DSceneStore();
var panel = scene.CreateNode(draw.Finish());
var sceneInput = graph.CreateInput(
    "ui.scene", Draw2DSceneInputContract.Instance);
var ui = graph.Add2DPass("settings", new Draw2DPassRequest(sceneInput, color));
graph.MarkOutput(ui.Color);
var plan = graph.Compile();
var inputs = plan.CreateBindings();
inputs.Set(sceneInput, scene.Snapshot(deviceScale: 1.5f));
var bindings = inputs.Build();
using var first = await runtime.SubmitAsync(plan, bindings, cancellationToken);

scene.SetTransform(panel, System.Numerics.Matrix3x2.CreateTranslation(8, 0));
inputs.Set(sceneInput, scene.Snapshot(deviceScale: 1.5f));
bindings = inputs.Build();
using var second = await runtime.SubmitAsync(plan, bindings, cancellationToken);
```

文字と画像を組み立てるコードに shader、atlas、GPU handle は現れない。二度目は panel の transform だけが変わり、図形、文字、画像と共通 plan を共有する。次の frame で何も変わらなければ同じ bindings を再提出する。新しい frame の編集は、提出済みの scene を変更しない。この例は CPU upload data の画像だけを使うため ReadTextures を省略できる。graph の画像を使う slot では `new Draw2DPassRequest(sceneInput, color, ReadTextures: [source])` のように利用可能な集合を固定する。

## 適合試験と移行順

最初に CPU scene と snapshot、画像／glyph／text の不変 upload data と測定済み metrics の受渡しを定義する。font 読込みと shaping／layout は `Lumyte.Resources` の分野へ分離する。次に Native／Portable それぞれで単純図形・画像を通し、path／stroke／paint、clip／layer／composite、文字と color glyph、保持 scene と cache の順に必要な能力を満たす。削除前に部分実装だった stroke と描画要素の組合せも新契約の完了条件に含める。追加提案はこの基礎を確認してから別の実装単位として進める。

同じ consumer assembly から両 provider を実行し、図形、curve、dash／join／cap、全 composite、入れ子 clip、mask／blur／shadow、glyph と palette、scene 編集後の snapshot 不変性を検証する。atlas 退役中の再利用、glyph／画像の Key 切替えと graph 画像の依存も対象とする。供給側の cache eviction 後も scene を描画できること、GPU cache を Trim した後に同じ data から再転送できること、描画中に font／画像の再ロードが生じないことを確認する。command 数、GPU struct 配置、shader 全文、物理 atlas 座標の一致は条件にしない。

同じ plan の入力更新と、各 scene から新しく作った plan の描画結果を比較する。transform だけの変更、内容の差し替え、visible／order／clip、layer の mask／blur／backdrop、DeviceScale の変更を別々に確認する。固定 ReadTextures 内の差し替えが可能であること、集合外の論理画像を入力しても未宣言の依存を追加できないことを検証する。新旧 snapshot の同時実行、飛ばした世代、古い snapshot への巻き戻し、初回 runtime、cache 回収後も完全な scene を描くことを確認する。

性能確認には、大量の静的 node のうち一つだけの transform を毎 frame 更新する workload と、変更のない workload を用意する。warm cache で静的画像／glyph の再転送がなく、CPU snapshot の確定と入力保持が全 node／glyph 数に比例する全走査を繰り返さないことを確認する。CPU allocation、snapshot／準備時間、変更した upload byte 数を測定し、描画負荷と GPU 時間を分けて記録する。60 FPS の可否は実機と描画量で測定し、plan の再利用だけで保証したとは扱わない。

native API／WebGPU が判断する usage、binding、shader、pipeline の合法性は独自 validator で重複確認しない。Lumyte は CPU scope の釣合い、描画値の意味、不変 upload data の Key、graph の依存、node 世代と使用保持という自分の契約を確認する。

## 採用範囲と未実装事項

削除前に調査した TwoD／Text の描画能力を目標とし、共通 CPU scene と同一 binary の追加 API を定義する。pass 本体、shader、GPU data と資源管理を Native／Portable の二系統へ分ける。利用者による GPU 描画経路と atlas 管理は要求しない。

新しい共通 scene／text／glyph／画像 upload 型と owning snapshot の受渡し、Add2DPass、型付き scene 入力と固定外部依存の契約、変更 page／部分木と所有集合を共有する store、再利用可能な bindings／plan、二系統の本体と shader、内部 graph、失効範囲を守る差分更新、atlas／glyph の使用保持、適合試験と継続的な部分更新の性能確認は未実装である。dash／join／cap／miter、全描画要素での拡張 gradient／path clip も新設する必要がある。削除前に実装があった項目も、新しい二系統で完成したとは扱わない。描画品質の許容値と対象 format の適合表は実装時に固定する必要がある。

image brush と nine-slice は追加提案であり未実装である。font ロード・shaping／段落 layout・hit testing、SVG のロード／decode の設計は `Lumyte.Resources` の分野であり、この ADR の採用範囲に含めない。縦書き・ruby、完全な SVG／外部 2D library との互換、IME／編集／widget の実装も責務外とする。
