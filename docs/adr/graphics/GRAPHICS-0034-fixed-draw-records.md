# ADR-GRAPHICS-0034: 描画を同じ長さの record にまとめ、引数で描画範囲を選ぶ

- 状態: 提案
- 日付: 2026-11-12
- 基準コミット: `1dbf62977a4805cb32d97811f8a890237bea20cb`
- 変更関係: 一部変更
- 変更元: [2D 描画](GRAPHICS-0018-2d-graphics-engine.md#決定)の内部描画表現、[共有登録表](GRAPHICS-0032-drawing2d-shared-argument-tables.md#materializemodule所有期間)の buffer 非統合方針
- 変更対象: 組込み Drawing2D の GPU 描画格納と実行範囲
- 継続する判断: 公開描画 API、登録表の容量・所属・所有期間、[CPU clip 正規化](GRAPHICS-0033-drawing2d-normalized-clips.md#決定)の数値規則と予算

## 背景

Drawing2D を保守する利用者は、異なる図形を連続格納し、その一部だけを描画したい。現在の汎用経路は一つの描画を図形・paint・積の式命令へ展開し、矩形専用経路は別の形式を使うため、同じ長さの描画配列として扱えない。

基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Rendering/DrawingProgram2D.cs` の `Compile` は図形と paint に `Multiply` を追加する。`Rendering/DrawingInstruction.cs` と `Shaders/drawing-fragment.slang` の構造は float4 六個、uint 四個、texture/sampler 参照二個であり、基準 SHA の `src/Graphics/Lumyte.Graphics.Vulkan/VulkanShaderReferenceEncoding.cs` の `Pack` が返す 16 B 参照 wire では 144 B/式命令となる。単色矩形の汎用経路は通常三命令、432 B に geometry 等が加わる。一方 `Rendering/PreparedRectangleBatch2D.cs` の `SolidBatch.Fill` は矩形一個に float4 六個、96 B と別 clip payload を使う。Rendering と Shaders の相対パスは同じ Drawing2D プロジェクト配下であり、数値は格納形式の算術であって時間の測定結果ではない。

## 決定

一つの論理描画を **96 B の DrawRecord2D** にし、すべての組込み描画を同じ配列へ実行順に詰める。paint、正規化 clip、可変 geometry は別の共有領域へ分離し、既存の引数設定に先頭 record と個数を渡して連続範囲を描く。単純図形の逆変換と形状値は record 内に置き、小さな固定ヘッダーだけを作って全図形に追加 geometry fetch を強制する案は選ばない。

### 利用者の変更前・変更後

内部変更なので、利用者の Canvas、Renderer2D、DrawingScene2D、retained content の記述、準備・Record・submit・完了待ち・解放は変わらない。公開 API の追加・削除・保証変更はない。内部の prepared batch は、現在の一描画一 interpreter 実行から、互換な連続 record 範囲のインスタンス実行へ変わる。custom renderer の独自 batch までこの格納形式へ強制しない。

変更前の内部呼び出しは、基準 SHA の `Rendering/PreparedGpuBatch2D.cs` の `RecordCommand` で次の形を取る。pipeline/state、引数生成、upload、所有者と失敗処理は抜粋外である。

```csharp
command.Reservation.Draw(render, in arguments, 3);
```

変更後の提案例では、admission 済みの同じ table と pipeline を使い、ページの 12 番から 5 個、半開区間 [12,17) を選ぶ。初期化・所有・admission は後述の契約に従い、この抜粋で生成や解放を代用しない。

```csharp
// Lumyte.Graphics.Drawing2D 内部。using Lumyte.Graphics.Abstractions;
var selected = pageArguments with { FirstRecord = 12, RecordCount = 5 };
render.SetArgumentTable(table);
render.SetArguments(in selected);
render.Draw(vertexCount: 6, instanceCount: selected.RecordCount,
    firstVertex: 0, firstInstance: 0);
```

### 96 B の格納契約

record は旧式命令ではなく一つの描画である。shader 配列の element alignment は 16 B、stride は正確に 96 B とし、C# の managed sizeof を wire の根拠にしない。数値以外の GPU 参照は record に埋めず、下表の uint ID はページ内の論理索引である。

| byte offset | 長さ | 内容 |
| --- | --- | --- |
| 0 | 16 | uint Kind、Flags、PaintId、ClipId（各 4 B） |
| 16 | 16 | float4 DeviceBounds: target-local の left, top, right, bottom |
| 32 | 16 | float4 InverseX: inverse.M11, M21, M31, 0 |
| 48 | 16 | float4 InverseY: inverse.M12, M22, M32, 0 |
| 64 | 16 | float4 Shape0 |
| 80 | 16 | uint PayloadOffset16、PayloadCount、Mode、ExtraBits |

隙間はない。InverseX/Y の w、未使用 shape lane、未使用 payload/Mode/ExtraBits、未定義 Flags bit はすべて 0 に初期化する。ExtraBits は丸角の radius の float32 bit を保持する場合だけ使う。Kind と Flags の未知値を準備時に拒否し、型の異なる lane を数値変換で読み替えない。PaintId/ClipId の 0 はそれぞれ初期化済み透明 paint / clip なしを表す。

| 現行 Compile の command | Kind と inline 値 | 別領域 |
| --- | --- | --- |
| FillRect | Rect、Shape0 = x,y,width,height | paint、必要な clip のみ |
| FillRoundedRect | RoundedRect、Shape0 = rect、ExtraBits = clamp 済み radius | paint、clip |
| FillCircle | Circle、Shape0 = center.x,center.y,radius,0 | paint、clip |
| DrawLine | StrokeTriangles、Shape0 = 0 | 現行の cap/join/dash による stroke 三角形 |
| FillPath | PathEdges、Shape0 = 0、Mode = fill rule | 平坦化辺 float4 の列 |
| StrokePath | StrokeTriangles、Shape0 = 0 | 現行の stroke 三角形 |
| DrawImage | Rect、Shape0 = destination | source、image transform、画像設定は専用 image paint |
| DrawCoverageMask | CoverageMask、Shape0 = destination | source/view/sampler/寸法の mask descriptor |
| DrawDistanceField | DistanceField、Shape0 = destination | source/view/sampler/寸法/encoding/range の field descriptor |
| DrawMesh | MeshTriangles、Shape0 = 0、Mode = center coverage | index 展開済み position/coverage 三角形 |
| DrawPaintGraph | PaintGraph、Shape0 = 0、逆変換は identity | paint graph。保守的 bounds がなければ target 全域 |

PayloadOffset16 は NumericPayload の 16 B 単位位置、mask/field Kind では MaskDescriptors の byte offset/16 とする。image/field paint の directory entry も ImagePaintData の byte offset/16 を用い、それ以外の paint と clip は NumericPayload 相対とする。typed descriptor の開始位置はその reflected stride と 16 B の両方に整列し、shader では offset*16/stride を索引にする。mask/field descriptor と image/field paint の双方で offset*16 が stride で割り切れない値を準備時に拒否する。directory の image/field PayloadLength16 は reflected stride/16 と等しく、offset と長さの合計が ImagePaintData の有効 byte 長以下であることを checked 64 bit で検査する。mask descriptor も count=1 と reflected stride の境界を同様に検査する。PayloadCount は辺数、三角形数、または descriptor 一個を表す。各 Kind の消費長を checked 演算で検査する。StrokeTriangles は三角形あたり float4 三個、MeshTriangles も同形式とし、mask descriptor は source float4（x,y,width,height）、texture 寸法/range float4（width,height,distanceRange,0）、uint encoding/reserved 四個、GPU texture/sampler 参照の typed 部分からなる独立型とする。GPU 参照を raw uint へ cast せず、生成 serializer が実際の wire stride を決める。mask/field は追加 descriptor fetch を負担するが、単純図形には発生しない。

無効な inverse、空 clip、面積ゼロの image/mask/field は従来の透明・空の扱いを維持し、通常描画なら record 確保前に除く。shape の local 座標、paint 固有変換、clip の捕捉変換を混同しない。

### paint と clip の独立表現

PaintId は同ページの paint directory を指す。directory entry は uint Kind、PayloadOffset16、PayloadLength16、Flags の 16 B とし、型別 payload を 16 B 境界に置く。無条件に全 paint node を旧 144 B にしない。payload は単色の線形 premultiplied float4、gradient の逆変換・幾何値・extend・stop 範囲、image の逆変換・source・寸法・tile/filter/alpha・typed texture/sampler 参照、子 ID と係数を持つ opacity、二子 ID と mode を持つ composite、子 ID と ClipId、field descriptor と子 ID に分ける。transform は各 leaf の累積変換へ畳み、各中間変換の有限 inverse 検査を維持する。Layers は順序付き SrcOver の graph として保持する。

paint 共有 key は不変 node identity、累積変換、target 情報、資源 revision/generation とし、同一ページかつ同じ table でのみ共有する。ImagePaintData は image/field leaf 共通型とし、逆変換、source、寸法/tile、filter/alpha/field mode/range、typed texture/sampler 参照を持ち、field の子 ID はその leaf payload に保持する。評価順は子先行、循環拒否、深さ 64 を保つ。一描画あたりの旧表現相当の式作業量（shape、paint、外側 Multiply と残余 clip を含む）の上限 256 と Data 合計予算も preflight で保ち、固定長化だけで受理上限を拡大しない。Data 合計は一描画の旧 Compile 相当の path/stroke/mesh geometry、gradient stops、外側と paint 子の clip mask を合算し、共有資源であっても各描画の到達分を算定する。旧 paint cache と同じ node identity/累積変換で一度だけ計数し、それ以外の重複は旧展開どおり計数する。式作業も旧 Compile 相当の形状・paint 展開・Multiply・正規化 clip を同じ cache key で数える。新しい directory/descriptor の bytes はこの旧 Data 換算へ混ぜず、別途物理 pool と retained 総予算へ加算する。outer shape×paint 用 Multiply は削除するが、paint 内の composition、opacity、distance-field coverage と子 clip は独立 node のまま残す。

gradient stop は offset float4 と線形 premultiplied RGBA float4 の **32 B/stop** を維持し、現行の解析的な stop 補間・色空間・精度を変更しない。texture/sampler は paint/descriptor に一度だけ格納し、ページの登録表に属する IGpuRef として materialize 後に pack する。

ClipId は uint Kind、PayloadOffset16、PayloadLength16、Reserved の 16 B directory を指す。None は payload なし、Scissor/Analytic は確定 sample 添字の uint4、SampleMask は pixel bounds uint4 と mask offset/count/reserved の uint4 を持つ。mask は現行と同じ 16 bit membership を数値 float lane に格納し 4 B/画素とする。CPU 正規化の分類、予算、FMA・fill rule・origin/DPR は変更しない。outer の整数 clip は bounding quad を整数境界で絞れば同 batch に混在できる。paint 子の clip はその子の値だけへ適用し、opacity/composite 境界を越えて外側 scissor にしない。

各画素の s=(x+(i+0.5)/4,y+(j+0.5)/4)、i,j=0..3 について、**sum(shape(s) * paint(s) * clip(s)) / 16** を出力する。DrawMesh の coverage は基準 shader の Mode=1 と同じ画素中心で求め、その値と各サンプルの paint/clip を積にする。shape と clip の別々の平均を掛けない。record と descriptor の不変値を sample loop 外で取得し、毎サンプルで旧 144 B 式 record を読み直す構造を残さない。ただし実際の load 削減やレジスタ使用量は compiler/backend の検証事項である。

### 範囲と painter order

引数は Records、paint/clip directory と各 payload 参照、target の size/origin、および uint FirstRecord/RecordCount を持つ。すべての ID と payload offset は **ページ内の対応 stream の先頭相対**であり、FirstRecord 相対ではない。paint の子 ID も同様で、部分描画によって再採番しない。paint graph は root から到達する node だけを子先行で評価し、ページ全 paint を各 record で評価しない。記録時に FirstRecord <= page.RecordCount、RecordCount <= page.RecordCount - FirstRecord を検査し、積と byte range は checked 64 bit で計算する。空範囲は任意の有効先頭（末尾を含む）で許容し、SetArguments/Draw を省略する。非空の ShaderDataSlice にゼロ長を渡さない。無効範囲は native 命令の記録前に ArgumentOutOfRangeException とし、それまで有効だった引数選択を変更しない。

vertex shader は SV_InstanceID に FirstRecord を一度だけ足し、範囲の record を読み、DeviceBounds の quad を六頂点で作る。flat/nointerpolation の record index と必要な値を fragment へ渡す。firstInstance は常に 0 とし二重 offset を避ける。quad は従来の coverage を切らない保守的な整数画素境界へ広げ、二三角形の共通辺で二重 blend しない規則を検証する。fragment は当該 record の Kind 分岐と geometry 評価だけを行い、全描画 record を画素ごとに走査しない。path/stroke/mesh は現行の辺・三角形 evaluator を使い、native mesh rasterization による AA 変更は行わない。

record の配列順を painter order とする。種類順 sorting、別々の draw の合成を一度の sample average に変える処理は禁止する。各 instance の 16 サンプルを平均した後、従来と同じ target blend を行う。透明な重なりのある異なる instance の attachment blend が instance/primitive 順を保つことを backend の必要契約とし、native API の順序保証の確認とハードウェア受け入れ試験を導入条件にする。確認できない backend は record ごとの Draw を同順に発行し、格納形式は維持する。

基準 SHA の `src/Graphics/Lumyte.Graphics.Abstractions/IRenderEncoder.cs` の Draw は instanceCount/firstInstance を持ち、`src/Graphics/Lumyte.Graphics.Vulkan/VulkanRenderEncoder.cs` と `src/Graphics/Lumyte.Graphics.Wgpu/WgpuRenderEncoder.cs`、`src/Graphics/Lumyte.Graphics.Direct3D12/Direct3D12RenderEncoder.cs` の `Draw` は native の引数へ渡す。これは instancing の入口の根拠であり、全 backend の pixel/order 受け入れ完了を意味しない。

連続範囲を一 Draw にする条件は同じ pipeline、blend、target、table、ページ、互換な state である。異なる Kind は一 pipeline の分岐で処理できるが、effect、layer の一時 target、backdrop read、異なる blend/scissor、table 容量、pass 境界は同順の範囲へ分割する。一範囲が必ず一 native draw になる保証はしない。graph の Source/Original/Opacity/GraphMode による合成は既存 pass の責務を保ち、対象範囲の clip coverage を固定長 record で供給する。effect kernel 自体は drawing record ではない。

### ページ、容量と登録の順序

通常 record 領域の初期方針を **32 KiB = 32768 B** とする。96 B stride なので 341 record = 32736 B、末尾 32 B は shader から参照しない未使用領域となる。typed allocation 自体は 341 要素 = 32736 B とでき、arena が 32 KiB を予約する場合だけ tail 32 B をゼロ化する。341 個を越えたら次ページの FirstRecord=0 から継続する。異なる種類ごとの GPU allocation、clip ごとの allocation は作らない。この容量は設計上の初期値であり、最適な値や性能改善を保証する値ではない。

binding 開始 alignment と element stride を区別する。全配列を bind して FirstRecord で選び、各 record を 256 B へ pad しない。arena 内に typed ページを置くなら、開始 byte offset は backend の binding alignment と 96 B の最小公倍数へ揃える。例として alignment=256 B なら 768 B 境界であり、32768 B の直後へ次ページを機械的に置かない。typed offset は byte offset/96 の整数でなければならない。MaxBufferSize、最大 binding range、shader の count/offset 幅を毎 allocation で検査する。基準 SHA の `src/Graphics/Lumyte.Graphics.Abstractions/ShaderDataSlice.cs` は offset/count を logical element とする。

paint、clip、geometry は数値 stream と参照を持つ typed descriptor stream に分けた append pool を使い、32 KiB の record 領域に押し込まない。通常 pool の block も初期値 32 KiB とするが、単一の連続 payload が大きい場合は必要長を alignment へ切り上げた専用領域を使う。既存の Data 合計 262144 float4/描画、clip mask/CPU 作業量、矩形 payload 32 MiB 等の予算と backend 上限をすべて満たす場合だけ許可する。参照は領域先頭をページ metadata で確定し、一 payload の offset はその参照の先頭相対とする。非連続 chunk を shader が連続配列と誤認する分割は行わず、単一 payload が上限超過なら準備を拒否する。pool の block 数、専用領域、CPU staging と GPU device copy、in-flight、idle retention はそれぞれ予算へ計上する。

実行順の候補区間について root と到達する全 paint/clip/geometry/texture/sampler の容量を preflight し、登録表を予約してから storage を確保する。次の record 全体を admit できなければ直前で区間を閉じ、次の表とページへ進める。一描画単独で超過する場合は native Write 前に拒否する。全 materialize 成功後に参照を解決し、最後に payload/record を pack する。upload 済みの wire を別 table へ root の変更だけで rebase しない。

固定長 storage の統合と logical batch の統合は別である。Graph は既存の一 pass 宣言全体を容量単位とし、組込み処理が事前に pass を分割する。手動 AddPass の宣言超過を自動分割しない。record buffer、paint 評価命令の buffer、native command buffer、graph の pass 登録表を別の所有単位として扱う。間接到達する資源も各 pass に標準 Use 宣言を行い、copy-write producer と shader-read、culling、barrier を省略しない。

### 所有権と retained 更新

準備中の CPU geometry/mask cache と pack plan は準備限定の所有者に置き、upload または最後の遅延 pack 後に mask 配列と辞書容量を解放する。失敗、取消、append 中断、graph abandon/culling でも解放する。小さな生存 retained asset が無関係な大きい mask を pin しないことを維持する。

完成ページは immutable snapshot とし、table と参照先資産を含む shared owner/refcount を持つ。GPU 完了 **かつ command disposal** 前には上書き・pool 返却しない。借用画像/view/sampler の所有者は従来どおり呼出し側であり、shared owner は内部で生成した資源の所有と借用参照の保持を区別し、borrowed 資源の Dispose を防ぐ新しい lease 保証は加えない。retained の部分更新はページ単位 copy-on-write とする。未変更ページは共有できるが、同ページの生存 record の再 pack、挿入・削除・並べ替えによる後続区間の再構築は起こり得る。登録表の所属が変わるページは関連 payload も再 pack する。毎 frame が O(dirty) になる保証や framebuffer damage redraw は追加しない。retained 予算には shared bytes の重複を避けつつ、CPU 保持分、GPU bytes、古い in-flight snapshot をすべて含める。

### 内部 API 差分

以下は提案する追加宣言の抜粋で、namespace は既存 Drawing2D と同じにする。既存 DrawingArguments は graph 合成用として維持し、汎用 command の生成だけを新引数へ移す。IRenderEncoder.SetArguments/Draw と IShaderData の公開契約は変えない。各 buffer の IGpuRef は通常の generated codec と選択済み table を使い、追加の公開 binding API を作らない。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
+    // 数値のみの 96 B wire。offset は「96 B の格納契約」に固定する。
+    internal readonly partial record struct DrawRecord2D(
+        uint Kind, uint Flags, uint PaintId, uint ClipId,
+        Vector4 DeviceBounds, Vector4 InverseX, Vector4 InverseY,
+        Vector4 Shape0, uint PayloadOffset16, uint PayloadCount,
+        uint Mode, uint ExtraBits) : IShaderData;
+
+    // 各参照の先頭はページ内で固定。範囲を変えても子 ID/offset は不変。
+    internal readonly partial record struct DrawRangeArguments2D(
+        IGpuRef<DrawRecord2D> Records,
+        IGpuRef<DrawPageMetadata2D> PageMetadata,
+        uint FirstRecord, uint RecordCount,
+        Vector2 TargetSize, Vector2 PixelOrigin) : IShaderArguments;
 }
```

PageMetadata は次の typed shader-data 一個を指す。directory と geometry の各ページは一つの連続領域を参照し、別 block に続く場合は metadata と描画ページを閉じて次へ進める。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
+    internal readonly partial record struct DrawPageMetadata2D(
+        IGpuRef<DrawDirectoryEntry2D> PaintDirectory, IGpuRef<DrawDirectoryEntry2D> ClipDirectory,
+        IGpuRef<Vector4> NumericPayload,
+        IGpuRef<MaskDescriptor2D> MaskDescriptors,
+        IGpuRef<ImagePaintData2D> ImagePaintData) : IShaderData;
+
+    internal readonly partial record struct DrawDirectoryEntry2D(
+        uint Kind, uint PayloadOffset16, uint PayloadLength16, uint Flags) : IShaderData;
+
+    // destination は DrawRecord2D.Shape0。未使用 mode/range は 0。
+    internal readonly partial record struct MaskDescriptor2D(
+        Vector4 Source, Vector4 TextureSizeAndRange,
+        uint Encoding, uint Reserved0, uint Reserved1, uint Reserved2,
+        IGpuRef<IGraphicsTextureView> Texture,
+        IGpuRef<IGraphicsSampler> Sampler) : IShaderData;
+
+    // uint 群は child、tile、filter、alpha、field mode。正確な lane は pack schema で固定。
+    internal readonly partial record struct ImagePaintData2D(
+        Vector4 InverseX, Vector4 InverseY, Vector4 Source, Vector4 TextureSizeAndRange,
+        uint ChildId, uint TileX, uint TileY, uint Filter,
+        uint Alpha, uint FieldMode, uint Reserved0, uint Reserved1,
+        IGpuRef<IGraphicsTextureView> Texture,
+        IGpuRef<IGraphicsSampler> Sampler) : IShaderData;
 }
```

NumericPayload は geometry、paint 数値、clip 数値のページ内共通 16 B stream とする。directory の uint lane は uint4 wire を用い、NumericPayload 内で整数を扱う lane は bit-preserving な asuint/asfloat で読み書きし、float として数値 decode しない。mask の membership lane だけは既存の数値 float 方式を維持する。すべての reserved と未使用 lane を 0 にする。これら二つの参照付き型の wire offset/stride は backend reflection を正とし、DrawRecord2D の 96 B と混同しない。実装時に typed schema を揃え、全 backend の reflection と生成 codec で 96 B/各 field offset/array stride を照合することを必須とする。

## 検討した代替案

- 64 B record と外部 transform: record 密度は高いが、単純矩形にも逆変換を別 load させ、transform index と共有所有を追加する。単純図形の自己完結を優先して 96 B にする。
- 128 B record に image/mask 設定まで入れる: descriptor fetch を減らせるが、rect/circle/path も常に 32 B 多くなる。稀な大きい descriptor は別領域へ置く。
- 旧 144 B 式命令のサイズだけ統一する: 既存 evaluator を使いやすいが、一描画が複数 record のままで要求する描画範囲と一致しない。
- 種類別の可変長描画列: 無駄な lane を減らせるが、同じ stride と単純な範囲参照を満たさない。paint/geometry 側だけに可変長を認める。
- すべてを一 native Draw にする: 呼び出し数は減らし得るが、blend、table、target、effect の不可分境界を壊す。格納を統一し、互換区間だけをまとめる。

## 結果と影響

組込み描画の物理 storage と範囲参照を統一でき、共有 paint/clip を描画ごとに複製する必要を減らせる。追加の directory indirection、mixed Kind 分岐、別 payload pool と copy-on-write の責務が生じる。96 B は描画 record のみの値であり、paint/geometry/clip/staging/retention を含む総 bytes や速度の改善を意味しない。

### 受け入れ条件

- 全 command、全 paint constructor、graph layer/effect 合成を基準の公開 API pixel 出力と照合する。単色/gradient、半透明 overlap、各 composite、異なる子 clip、深さ 64 と超過、特異な中間変換、共有 paint の異なる transform を含める。
- backend reflection と generated serialization の offsets/stride を検査する。32 KiB の 0/1/340/341/342 record、first/last/空/範囲外/uint overflow、256 B binding alignment と 96 B stride、typed offset、buffer/range の上限と tail 初期化を検査する。
- instance の透明重なり・quad 共通辺・混在 Kind と record ごとの同順 Draw を比較する。Vulkan、D3D12、Wgpu/Browser の API 順序保証の根拠確認と実機試験を分け、未確認 backend を完了扱いにしない。
- disjoint half の shape/clip の積が 0、coincident half が 1/2 となること、gradient 相関、16 bit の各 lane、穴、DPR/origin、隣接 float 境界、DrawMesh の中心 coverage を独立 oracle で確認する。
- table exact/+1 と単一超過、transitive reference、paint sharing と別 table 再 pack、AddPass 超過拒否、組込み pass 分割、copy producer と culling を検査する。
- retained の旧 snapshot と複数 in-flight、挿入/削除/並べ替え、cache eviction、CPU/GPU byte accounting、upload failure/取消/graph abandon、完了前返却拒否と cleanup retry を構造的に検査する。

本書は設計の提案であり、上記の導入条件を満たした実装や backend reflection、pixel、性能の検証済みを意味しない。実装、benchmark、公開手続きは今回の決定対象外とする。
