# ADR-GRAPHICS-0034: 描画を同じ長さの record にまとめ、引数で描画範囲を選ぶ

- 状態: 採用
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

一つの論理描画を **96 B の DrawRecord2D** にし、すべての通常組込み描画を同じ配列へ実行順に詰める。paint、正規化 clip、可変 geometry は別の共有領域へ分離し、既存の引数設定に先頭 record と個数を渡して連続範囲を描く。単純図形の逆変換と形状値は record 内に置き、小さな固定ヘッダーだけを作って全図形に追加 geometry fetch を強制する案は選ばない。

### 利用者の変更前・変更後

内部変更なので、利用者の Canvas、Renderer2D、DrawingScene2D、retained content の記述、準備・Record・submit・完了待ち・解放は変わらない。公開シグネチャの追加・削除はない。MaxCachedBytes の共有・in-flight 所有分の計上は「所有権と retained 更新」の契約へ変更する。内部の prepared batch は、現在の一描画一 interpreter 実行から、互換な連続 record 範囲のインスタンス実行へ変わる。custom renderer の独自 batch までこの格納形式へ強制しない。

変更前の内部呼び出しは、基準 SHA の `Rendering/PreparedGpuBatch2D.cs` の `RecordCommand` で次の形を取る。pipeline/state、引数生成、upload、所有者と失敗処理は抜粋外である。

```csharp
command.Reservation.Draw(render, in arguments, 3);
```

変更後の内部呼び出し例では、admission 済みの同じ table と pipeline を使い、ページの 12 番から 5 個、半開区間 [12,17) を選ぶ。初期化・所有・admission は後述の契約に従い、この抜粋で生成や解放を代用しない。

```csharp
// Lumyte.Graphics.Drawing2D 内部。using Lumyte.Graphics.Abstractions;
var selected = pageArguments with { FirstRecord = 12, RecordCount = 5 };
render.SetArgumentTable(table);
render.SetArguments(in selected);
render.Draw(vertexCount: 6, instanceCount: selected.RecordCount,
    firstVertex: 0, firstInstance: 0);
```

### 96 B の格納契約

record は旧式命令ではなく一つの描画である。shader 配列の各要素の実際の先頭を 16 B 境界へ置き、stride は正確に 96 B とする。これは stream 開始位置と stride による保証であり、compiler の natural-layout reflection が報告する型の最小 alignment と同一であることは要求しない。C# の managed sizeof を wire の根拠にしない。数値以外の GPU 参照は record に埋めず、下表の uint ID はページ内の論理索引である。

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

PayloadOffset16 の参照先、Kind ごとの消費長、typed descriptor の索引変換は「全 stream の byte 配置」を正本とする。mask/field は追加 descriptor fetch を負担するが、単純図形には発生しない。

無効な inverse、空 clip、面積ゼロの image/mask/field は従来の透明・空の扱いを維持し、通常描画なら record 確保前に除く。shape の local 座標、paint 固有変換、clip の捕捉変換を混同しない。

### paint と clip の独立表現

PaintId は同ページの paint directory を指す。directory entry と型別 payload の正確な配置は「全 stream の byte 配置」に従う。無条件に全 paint node を旧 144 B にしない。payload は単色の線形 premultiplied float4、gradient の逆変換・幾何値・extend・stop 範囲、image の逆変換・source・寸法・tile/filter/alpha・typed texture/sampler 参照、子 ID と係数を持つ opacity、二子 ID と mode を持つ composite、子 ID と ClipId、field descriptor と子 ID に分ける。transform は各 leaf の累積変換へ畳み、各中間変換の有限 inverse 検査を維持する。Layers は順序付き SrcOver の graph として保持する。

paint 共有 key は不変 node identity、累積変換、target 情報、資源 revision/generation とし、同一ページかつ同じ table でのみ共有する。ImagePaintData は image/field leaf 共通型とし、逆変換、source、寸法/tile、filter/alpha/field mode/range、typed texture/sampler 参照を持ち、field の子 ID はその leaf payload に保持する。評価順は子先行、循環拒否、深さ 64 を保つ。一描画あたりの旧表現相当の式作業量（shape、paint、外側 Multiply と残余 clip を含む）の上限 256 と Data 合計予算も preflight で保ち、固定長化だけで受理上限を拡大しない。Data 合計は一描画の旧 Compile 相当の path/stroke/mesh geometry、gradient stops、外側と paint 子の clip mask を合算し、共有資源であっても各描画の到達分を算定する。旧 paint cache と同じ node identity/累積変換で一度だけ計数し、それ以外の重複は旧展開どおり計数する。式作業も旧 Compile 相当の形状・paint 展開・Multiply・正規化 clip を同じ cache key で数える。新しい directory/descriptor の bytes はこの旧 Data 換算へ混ぜず、別途物理 pool と retained 総予算へ加算する。outer shape×paint 用 Multiply は削除するが、paint 内の composition、opacity、distance-field coverage と子 clip は独立 node のまま残す。

gradient stop は offset float4 と線形 premultiplied RGBA float4 の **32 B/stop** を維持し、現行の解析的な stop 補間・色空間・精度を変更しない。texture/sampler は paint/descriptor に一度だけ格納し、ページの登録表に属する IGpuRef として materialize 後に pack する。

ClipId は独立した clip directory を指し、payload と membership の配置は「全 stream の byte 配置」に従う。CPU 正規化の分類、予算、FMA・fill rule・origin/DPR は変更しない。outer の整数 clip は bounding quad を整数境界で絞れば同 batch に混在できる。paint 子の clip はその子の値だけへ適用し、opacity/composite 境界を越えて外側 scissor にしない。

各画素の s=(x+(i+0.5)/4,y+(j+0.5)/4)、i,j=0..3 について、**sum(shape(s) * paint(s) * clip(s)) / 16** を出力する。DrawMesh の coverage は基準 shader の Mode=1 と同じ画素中心で求め、その値と各サンプルの paint/clip を積にする。shape と clip の別々の平均を掛けない。record と descriptor の不変値を sample loop 外で取得し、毎サンプルで旧 144 B 式 record を読み直す構造を残さない。ただし実際の load 削減やレジスタ使用量は compiler/backend の検証事項である。

### 全 stream の byte 配置

以下を canonical ABI とする。offset は各要素または payload 先頭からの byte、範囲は半開区間、スカラーは little-endian の 32 bit である。float4 は各 lane が float32、uint4 は各 lane が uint32、いずれも lane offset は 0/4/8/12 B である。表の長さは padding 込みで正確な長さであり、明記した reserved、tail、未使用 lane はゼロにする。数値 stream の alignment は 16 B とする。

NumericPayload は raw の `float4` 配列（16 B/cell）一つであり、以下の paint、clip、stops、geometry、membership はその内部の論理領域であって独立 binding ではない。整数 lane は uint32 の wire bits を float32 storage へ bitcast し、shader で asuint する。CPU staging/upload は NaN を含む全 bit を保存し、float 数値変換・演算・正規化を介さない。membership だけは後述の数値 float である。新しい raw uint pointer API を前提にしない。

#### directory と ID

| stream / stride | byte offset | 長さ | wire と意味 |
| --- | --- | --- | --- |
| PaintDirectory / 16 B | 0 | 4 | uint Kind |
| 同上 | 4 | 4 | uint PayloadOffset16 |
| 同上 | 8 | 4 | uint PayloadLength16（16 B cell 数） |
| 同上 | 12 | 4 | uint Flags = 0 |
| ClipDirectory / 16 B | 0 | 4 | uint Kind |
| 同上 | 4 | 4 | uint PayloadOffset16 |
| 同上 | 8 | 4 | uint PayloadLength16 |
| 同上 | 12 | 4 | uint Reserved = 0 |

Draw Kind は本決定で Rect=1、RoundedRect=2、Circle=3、PathEdges=4、StrokeTriangles=5、MeshTriangles=6、CoverageMask=7、DistanceField=8、PaintGraph=9 とする。Flags は現版では 0 のみ。Path Mode は NonZero=0 / EvenOdd=1、Mesh Mode は 1、他の Mode は 0 とする。PaintGraph の shape coverage は 1、逆変換は identity、geometry payload は空であり、terminal paint は DrawRecord.PaintId で指定する。

paint Kind は本決定で Transparent=0、Solid=1、Linear=2、Radial=3、Sweep=4、Image=5、Opacity=6、Composite=7、Clip=8、DistanceField=9 とする。clip Kind は None=0、Scissor=2、Analytic=3、SampleMask=4（基準 NormalizedClipKind2D と同値）、Empty=1 は準備時に除き GPU へ送らない。未知 Kind/enum/flag は拒否する。PaintDirectory[0] と ClipDirectory[0] は全 16 B をゼロにし、payload を読まない透明 paint / clip なしとする。Solid の透明値は別の有効 payload としても表せる。ID は全て uint32 の directory element index、0 以外は有効 count 未満でなければならない。

#### NumericPayload の paint payload

下表の各 offset は directory の PayloadOffset16 * 16 からの相対 byte である。float と uint の混在 cell も 16 B を占める。全長と PayloadLength16 を右端で固定する。

| Kind | byte offset | 長さ | lane 内容 | 全長 / Length16 |
| --- | --- | --- | --- | --- |
| Solid | 0 | 16 | float4 linear premultiplied R,G,B,A | 16 B / 1 |
| Linear / Radial / Sweep | 0 | 16 | float4 InverseX: M11,M21,M31,0 | 下記 |
| 同上 | 16 | 16 | float4 InverseY: M12,M22,M32,0 | 下記 |
| Linear | 32 | 16 | float4 P0.x,P0.y,P1.x,P1.y | 80 B / 5 |
| Linear | 48 | 16 | float4 P2.x,P2.y,0,0 | 同上 |
| Linear | 64 | 16 | uint StopOffset16,StopCount,Extend,0 | 同上 |
| Radial | 32 | 16 | float4 C0.x,C0.y,R0,0 | 80 B / 5 |
| Radial | 48 | 16 | float4 C1.x,C1.y,R1,0 | 同上 |
| Radial | 64 | 16 | uint StopOffset16,StopCount,Extend,0 | 同上 |
| Sweep | 32 | 16 | float4 Center.x,Center.y,StartAngle,EndAngle | 64 B / 4 |
| Sweep | 48 | 16 | uint StopOffset16,StopCount,Extend,0 | 同上 |
| Opacity | 0 | 16 | uint ChildId at 0; float Opacity at 4; uint 0,0 at 8,12 | 16 B / 1 |
| Composite | 0 | 16 | uint SourceId,BackdropId,CompositeMode,0 | 16 B / 1 |
| Clip | 0 | 16 | uint ChildId,ClipId,0,0 | 16 B / 1 |
| Transparent | — | 0 | offset/length = 0、payload なし | 0 B / 0 |

Image と DistanceField は NumericPayload を消費せず、directory の offset/length は ImagePaintData の byte offset/16 と 8 を表す。StopOffset16 は NumericPayload の先頭相対で、StopCount は stop 数である。stop の消費 cell 数は 2*StopCount。空 stop 列は offset=0、count=0 で透明となり読まない。Extend は既存 Pad=0 / Repeat=1 / Reflect=2、角度は既存と同じ radian。P0/P1/P2 による線形投影、二円 radial の根選択、sweep のゼロ角幅と重複 stop の規則は基準 evaluator を維持する。

leaf inverse は target-local sample から paint-local（image なら source-relative texel）への累積逆変換であり、DrawRecord inverse（shape-local）で代用しない。Transform node は有限性を各段で検査して leaf へ畳む。Opacity は child * 係数、Composite は source/backdrop の指定順、Clip は child のみに ClipId の sample membership を掛ける。子 ID は親より小さいページ内 ID とし、到達 node を子先行に一度評価して root の値を返す。Layers は透明 ID=0 から各 child を source、直前の結果を backdrop とする SrcOver=3 の列へ下げる。CompositeMode は基準 Model/CompositeMode2D.cs の Clear=0 から HslLuminosity=27 の全値を保つ。深さ・式作業量の検査は共有後の directory count だけで代用しない。

#### typed image と mask/field descriptor

ImagePaintData は **128 B stride**、MaskDescriptors は **80 B stride** を本決定の固定 wire とする。texture/sampler の 16 B wire は後述の backend 表に従うが、論理値は IGpuRef のまま generated serializer と登録表で解決する。生 uint4 の resource ID を自作して参照を偽装しない。

| stream | byte offset | 長さ | 内容 |
| --- | --- | --- | --- |
| ImagePaintData | 0 | 16 | float4 InverseX: M11,M21,M31,0 |
| 同上 | 16 | 16 | float4 InverseY: M12,M22,M32,0 |
| 同上 | 32 | 16 | float4 Source: x,y,width,height（選択 mip の texel） |
| 同上 | 48 | 16 | float4 TextureSizeAndRange: width,height,distanceRange,0 |
| 同上 | 64 | 16 | uint ChildId,TileX,TileY,Filter |
| 同上 | 80 | 16 | uint Alpha,FieldMode,Reserved0=0,Reserved1=0 |
| 同上 | 96 | 16 | GpuTextureRef Texture |
| 同上 | 112 | 16 | GpuSamplerRef Sampler |
| MaskDescriptors | 0 | 16 | float4 Source: x,y,width,height |
| 同上 | 16 | 16 | float4 TextureSizeAndRange: width,height,distanceRange,0 |
| 同上 | 32 | 16 | uint Encoding,Reserved0=0,Reserved1=0,Reserved2=0 |
| 同上 | 48 | 16 | GpuTextureRef Texture |
| 同上 | 64 | 16 | GpuSamplerRef Sampler |

image は ChildId/FieldMode/distanceRange=0。TileX/Y は Clamp=0 / Repeat=1 / Mirror=2 / Decal=3、Filter は Nearest=0 / Linear=1、Alpha は Straight=0 / Premultiplied=1。source-relative texel の tile/filter と straight RGB の alpha 乗算を基準 ImageInstruction/imagePattern と同じ順に行う。DrawImage は基準 Compile と同じ viewTransform を使い、source crop を単に destination inverse へ取り違えない。

DistanceField paint は ChildId と正の distanceRange を使い、FieldMode は既存 Msdf=0 / Mtsdf=1。画像の数値 channel は sRGB/alpha 変換を行わず、基準の median RGB、MTSDF minification、affine footprint と 1/4 pixel ramp で child に掛ける。shape 用 MaskDescriptors の Encoding は既存 Sdf=0 / Msdf=1 / Mtsdf=2。CoverageMask は Encoding/range=0 で赤 channel を用い、shape DistanceField は基準 SampleMask evaluator の encoding 分岐・range/rate を維持する。両者を paint 用 field evaluator と統合して再構成 filter を変更しない。

MaskDescriptors の destination と shape inverse は DrawRecord の値を使い、local destination から Source への写像とその微分で texture sample を取る。PayloadCount は 1、消費長は 80 B。image/field directory の Length16 は 8。typed index はそれぞれ PayloadOffset16/5、PayloadOffset16/8 とし、割り切れない offset は拒否する。末尾も含め checked 64 bit で有効 byte 長内を検査する。

#### clip、stop、geometry、membership

| 論理領域 | byte offset | 長さ | 内容 / element stride |
| --- | --- | --- | --- |
| Scissor / Analytic payload | 0 | 16 | uint4 sampleLeft,sampleTop,sampleRight,sampleBottom / 16 B |
| SampleMask payload | 0 | 16 | uint4 pixelX,pixelY,width,height |
| 同上 | 16 | 16 | uint MaskOffset16,MaskCellCount,0,0 / 全体 32 B |
| gradient stop | 0 | 16 | float4 offset,0,0,0 |
| 同上 | 16 | 16 | float4 linear premultiplied R,G,B,A / 32 B |
| path edge | 0 | 16 | float4 start.x,start.y,end.x,end.y / 16 B |
| stroke triangle | 0 | 16 | float4 a.x,a.y,1,0 |
| 同上 | 16 | 16 | float4 b.x,b.y,1,0 |
| 同上 | 32 | 16 | float4 c.x,c.y,1,0 / 48 B |
| mesh triangle | 0 | 16 | float4 a.x,a.y,a.coverage,0 |
| 同上 | 16 | 16 | float4 b.x,b.y,b.coverage,0 |
| 同上 | 32 | 16 | float4 c.x,c.y,c.coverage,0 / 48 B |
| membership cell | 0 | 16 | float4 exactIntegerMask0,Mask1,Mask2,Mask3 / 16 B、4 pixels |

clip directory の Length16 は Scissor/Analytic=1、SampleMask=2、None=0。sample bounds は target-local の確定済み 4 倍整数添字で right/bottom exclusive、Scissor は pixel bounds を 4 倍する。mask bounds は target-local pixel の非負整数であり、MaskOffset16 は同じ NumericPayload 先頭相対である。幅・高さを checked で掛け、MaskCellCount=ceil(width*height/4) を検査する。

membership は各 pixel の 16 bit、bit(j*4+i) がその AA sample の所属を表す。各 lane は [0,65535] の整数を **数値 float32** として格納し、uint(packed[lane]) で復元する（他の uint lane の bitcast と区別する）。q=(pixelY-boundsY)*width+(pixelX-boundsX)、cell=q/4、lane=q%4 の row-major とし、行末に padding を入れない。width が 4 の倍数でなくても次行が同じ cell の残 lane に続く。最終 cell の範囲外 lane のみゼロ。16 B/4 pixels = 4 B/pixel、実領域は 16*ceil(width*height/4) B である。

辺・三角形の座標は基準 flatten/mesh と同じ shape-local、coverage は float32。DrawRecord の PayloadCount は辺数または三角形数で、必要 cell 数は順に count、3*count、3*count。PayloadOffset16 は三角形の 48 B element index ではなく 16 B cell index のままで、48 B 境界を要求しない。PayloadCount=0 なら offset=0 で読まない。gradient stop も 32 B の独立 typed 配列ではなく二 cell 組なので、StopOffset16 を偶数に制限しない。

#### GPU 参照 wire と compiler の照合

基準 SHA の `tools/shaders/include/lumyte.slang` の `GpuBufferRef<T>`、GpuTextureRef、GpuSamplerRef は全て uint 四個の **16 B** である。GpuBufferRef.Count は byte 数でなく参照先 T の要素数。次表はこの既存 helper ABI の backend 差であって新しい portable address の提案ではない。

| backend / reference | +0 (4 B) | +4 (4 B) | +8 (4 B) | +12 (4 B) |
| --- | --- | --- | --- | --- |
| Vulkan buffer | device address low32 | address high32 | uint element count | 0 |
| Vulkan texture / sampler | uint table slot | 0 | uint reference count | 0 |
| D3D12 buffer / texture / sampler | uint table slot | uint OffsetInBytes | uint element/reference count | 0 |
| Wgpu / Browser 同上 | uint logical slot | uint (OffsetInBytes - RegistrationOffsetInBytes) | uint element/reference count | 0 |

Vulkan は address + slice offset、D3D12 は raw buffer の byte offset、WGSL は登録範囲相対 offset を使う。根拠は基準 SHA の `src/Graphics/Lumyte.Graphics.Vulkan/VulkanShaderReferenceEncoding.cs`、`src/Graphics/Lumyte.Graphics.Direct3D12/Direct3D12ShaderReferenceEncoding.cs`、`src/Graphics/Lumyte.Graphics.Shared/ShaderReferenceEncoding.cs` の Pack である。WGSL の remap は同 `Shared/ShaderBindingData.cs` の Map に従い、slot ごとに +0 texture、+4 sampler、+8 read buffer、+12 writable buffer の各 uint index を持つ 16 B row である。この backend 所有 binding buffer は Drawing2D の payload や追加 page metadata ではない。

基準 SHA の `src/Graphics/Lumyte.Graphics.Abstractions/ShaderSourcePreparation.cs` は `StructuredBuffer<Ptr<T>>` probe を追加し、buffer target schema を保存する。`src/Graphics/Lumyte.Graphics.Shared/ShaderDataLayout.cs` は natural-layout の field offset/size を読み、`src/Graphics/Lumyte.Graphics.Generators/ShaderArgumentsGenerator.cs` は名前付き value/reference の writer を生成する。したがって C# 宣言順の sizeof を wire とみなさず、各 target の schema と generated codec を上の **固定値へ照合**する。root uniform の layout も別に照合する。96/16/80/128 B と下記 80/64/144 B の offset/stride が一致しない backend は有効化しない。未知の reflected stride を受け入れて設計値を黙って変更しない。これは提案 ABI の受け入れ gate であり、新型の reflection が取得済みという主張ではない。

#### page metadata と全引数

DrawPageMetadata2D は **GPU typed element 一個、80 B stride** とし、CPU allocation directory とは区別する。CPU 側の有効長、capacity、owner、block 一覧、admission counter に新しい必須 GPU record は加えない。GPU が必要な各 stream count は参照内 Count にある。

| 型 | byte offset | 長さ | 内容 |
| --- | --- | --- | --- |
| DrawPageMetadata2D | 0 | 16 | `GpuBufferRef<DrawDirectoryEntry2D>` PaintDirectory |
| 同上 | 16 | 16 | `GpuBufferRef<DrawDirectoryEntry2D>` ClipDirectory |
| 同上 | 32 | 16 | `GpuBufferRef<float4>` NumericPayload |
| 同上 | 48 | 16 | `GpuBufferRef<MaskDescriptor2D>` MaskDescriptors |
| 同上 | 64 | 16 | `GpuBufferRef<ImagePaintData2D>` ImagePaintData |
| DrawRangeArguments2D | 0 | 16 | `GpuBufferRef<DrawRecord2D>` Records |
| 同上 | 16 | 16 | `GpuBufferRef<DrawPageMetadata2D>` PageMetadata（Count=1） |
| 同上 | 32 | 8 | uint FirstRecord at 32、RecordCount at 36 |
| 同上 | 40 | 8 | float2 TargetSize: width,height |
| 同上 | 48 | 8 | float2 PixelOrigin: x,y |
| 同上 | 56 | 8 | uint Reserved0=0、Reserved1=0 / 全体 64 B |

PixelOrigin は global graph pixel との変換用であり、通常 record の sample/DeviceBounds は target-local のままである。Records、directories、metadata、typed descriptors は IShaderData の ShaderDataSlice と generated serializer で登録する。NumericPayload の Vector4 は IShaderData ではないため `IGraphicsBuffer<Vector4>` の `BufferSlice<Vector4>` と `RegisterBuffer<T>` を用い、shader の `GpuBufferRef<float4>` target と 16 B stride を照合する。uint directory は scalar uint 四個の IShaderData とし、現行 writer に未対応な C# uint4 型を前提にしない。

graph 合成は新しい range arguments に GraphMode 等を暗黙追加せず、既存 DrawingArguments を使う別 route とする。その wire（基準 `Rendering/DrawingArguments.cs` と `Shaders/drawing-fragment.slang`、Drawing2D 配下）は次の **144 B**。effect kernel 自体の固有 arguments はこの描画 ABI に含めない。

| byte offset | 長さ | DrawingArguments の内容 |
| --- | --- | --- |
| 0 | 16 | `GpuBufferRef<DrawingInstruction>` Instructions |
| 16 | 16 | `GpuBufferRef<float4>` Data |
| 32 | 16 | uint Count at 32、Root at 36、GraphMode at 40、float Opacity at 44 |
| 48 | 16 | float4 SourceBounds: x,y,width,height |
| 64 | 16 | float4 OriginalBounds: x,y,width,height |
| 80 | 16 | float4 PixelOrigin: x,y,0,0 |
| 96 | 16 | GpuTextureRef SourceTexture |
| 112 | 16 | GpuTextureRef OriginalTexture |
| 128 | 16 | GpuSamplerRef GraphSampler |

この route の DrawingInstruction は上の背景に記した既存 144 B element（T0/T1/A/B/C/D が 0/16/32/48/64/80、Kind/First/Count/Mode が 96/100/104/108、Texture/Sampler が 112/128）を維持する。Data は同じ float4 stride 16 B。Count/Root は旧命令数/terminal index であり FirstRecord/RecordCount に読み替えない。GraphMode=0 は通常 evaluator、1 は Source*Opacity*coverage、2 はそれに Original*(1-coverage) を加え、totalCoverage=0 の fragment を discard する。Source/OriginalBounds と global pixel=p+PixelOrigin の契約を維持し、graph coverage の既存 clip interpreter を通常描画の新 record evaluator と混ぜない。通常描画全種の固定長化に対し、pass 合成専用のこの既存経路は明示的な例外とする。

有効化時には新 route の **7 read storage streams**（Records、PageMetadata、二 directory、NumericPayload、MaskDescriptors、ImagePaintData）と WGSL remap 一個で最大 **8 storage bindings/stage** を予算化する。空の payload stream も有効なゼロ初期化 dummy element 一個を登録し（参照 Count=1）、CPU 有効 count は 0 として payload から到達させない。dummy descriptor の texture/sampler 参照 lane はゼロ bit の偽参照にせず有効な fallback view/sampler を generated serializer で pack し、それ以外の lane をゼロにする。directory の ID=0 entry とは別の規則である。これらの dummy の bytes と登録も物理予算へ入れる。参照から辿る texture/sampler、root uniform 一個、backend 固有の予約 binding を併せて DeviceCaps と登録表の上限へ preflight する。metadata への indirection で binding count が減るとはみなさない。graph route は二 read streams + remap の最大 3 と Source/Original/GraphSampler を別に検査し、両 route の和を同一 draw の必須 binding 数としない。7+1 が backend 上限を超えるならこの構成を拒否し、storage 集約なしに適合すると主張しない。

stream 開始位置は binding alignment と各 stride（96/16/80/128、metadata 80）の最小公倍数へ揃える。16 B cell 内の論理 payload は別 binding ではなく 16 B alignment のみ。例として binding alignment=256 の MaskDescriptors は LCM(256,80)=1280 B、ImagePaintData は 256 B 境界である。offset16 の typed 変換と領域上限は開始・終端とも検査する。count と ID は uint32、byte 積と pool の合算は checked uint64。物理 bytes は record、directories、全 numeric cell、80/128 B descriptors、80 B metadata、dummy、argument upload、alignment/tail を含め、CPU staging/GPU/in-flight ごとに計上する。旧 Data 262144 float4 の admission は前節の旧 Compile 換算を維持し、新 descriptor の存在でその counter を増減させない。

### 範囲と painter order

引数は Records、paint/clip directory と各 payload 参照、target の size/origin、および uint FirstRecord/RecordCount を持つ。すべての ID と payload offset は **ページ内の対応 stream の先頭相対**であり、FirstRecord 相対ではない。paint の子 ID も同様で、部分描画によって再採番しない。paint graph は root から到達する node だけを子先行で評価し、ページ全 paint を各 record で評価しない。記録時に FirstRecord <= page.RecordCount、RecordCount <= page.RecordCount - FirstRecord を検査し、積と byte range は checked 64 bit で計算する。空範囲は任意の有効先頭（末尾を含む）で許容し、SetArguments/Draw を省略する。非空の ShaderDataSlice にゼロ長を渡さない。無効範囲は native 命令の記録前に ArgumentOutOfRangeException とし、それまで有効だった引数選択を変更しない。

vertex shader は SV_InstanceID に FirstRecord を一度だけ足し、範囲の record を読み、DeviceBounds の quad を六頂点で作る。flat/nointerpolation の record index と必要な値を fragment へ渡す。firstInstance は常に 0 とし二重 offset を避ける。quad は従来の coverage を切らない保守的な整数画素境界へ広げ、二三角形の共通辺で二重 blend しない規則を検証する。fragment は当該 record の Kind 分岐と geometry 評価だけを行い、全描画 record を画素ごとに走査しない。path/stroke/mesh は現行の辺・三角形 evaluator を使い、native mesh rasterization による AA 変更は行わない。

record の配列順を painter order とする。種類順 sorting、別々の draw の合成を一度の sample average に変える処理は禁止する。各 instance の 16 サンプルを平均した後、従来と同じ target blend を行う。透明な重なりのある異なる instance の attachment blend が instance/primitive 順を保つことを backend の必要契約とし、native API の順序保証の確認とハードウェア受け入れ試験を導入条件にする。確認できない backend は record ごとの Draw を同順に発行し、格納形式は維持する。

基準 SHA の `src/Graphics/Lumyte.Graphics.Abstractions/IRenderEncoder.cs` の Draw は instanceCount/firstInstance を持ち、`src/Graphics/Lumyte.Graphics.Vulkan/VulkanRenderEncoder.cs` と `src/Graphics/Lumyte.Graphics.Wgpu/WgpuRenderEncoder.cs`、`src/Graphics/Lumyte.Graphics.Direct3D12/Direct3D12RenderEncoder.cs` の `Draw` は native の引数へ渡す。これは instancing の入口の根拠であり、全 backend の pixel/order 受け入れ完了を意味しない。

連続範囲を一 Draw にする条件は同じ pipeline、blend、target、table、ページ、互換な state である。異なる Kind は一 pipeline の分岐で処理できるが、effect、layer の一時 target、backdrop read、異なる blend/scissor、table 容量、pass 境界は同順の範囲へ分割する。一範囲が必ず一 native draw になる保証はしない。graph の Source/Original/Opacity/GraphMode による pass 合成は「page metadata と全引数」の既存 DrawingArguments route を維持する。effect kernel 自体は drawing record ではない。

### ページ、容量と登録の順序

通常 record 領域の初期方針を **32 KiB = 32768 B** とする。96 B stride なので 341 record = 32736 B、末尾 32 B は shader から参照しない未使用領域となる。typed allocation 自体は 341 要素 = 32736 B とでき、arena が 32 KiB を予約する場合だけ tail 32 B をゼロ化する。341 個を越えたら次ページの FirstRecord=0 から継続する。異なる種類ごとの GPU allocation、clip ごとの allocation は作らない。この容量は設計上の初期値であり、最適な値や性能改善を保証する値ではない。

binding 開始 alignment と element stride を区別する。全配列を bind して FirstRecord で選び、各 record を 256 B へ pad しない。arena 内に typed ページを置くなら、開始 byte offset は backend の binding alignment と 96 B の最小公倍数へ揃える。例として alignment=256 B なら 768 B 境界であり、32768 B の直後へ次ページを機械的に置かない。typed offset は byte offset/96 の整数でなければならない。MaxBufferSize、最大 binding range、shader の count/offset 幅を毎 allocation で検査する。基準 SHA の `src/Graphics/Lumyte.Graphics.Abstractions/ShaderDataSlice.cs` は offset/count を logical element とする。

paint、clip、geometry は数値 stream と参照を持つ typed descriptor stream に分けた append pool を使い、32 KiB の record 領域に押し込まない。通常 pool の block も初期値 32 KiB とするが、単一の連続 payload が大きい場合は必要長を alignment へ切り上げた専用領域を使う。既存の Data 合計 262144 float4/描画、clip mask/CPU 作業量、矩形 payload 32 MiB 等の予算と backend 上限をすべて満たす場合だけ許可する。参照は領域先頭をページ metadata で確定し、一 payload の offset はその参照の先頭相対とする。非連続 chunk を shader が連続配列と誤認する分割は行わず、単一 payload が上限超過なら準備を拒否する。pool の block 数、専用領域、CPU staging と GPU device copy、in-flight、idle retention はそれぞれ予算へ計上する。

実行順の候補区間について root と到達する全 paint/clip/geometry/texture/sampler の容量を preflight し、登録表を予約してから storage を確保する。次の record 全体を admit できなければ直前で区間を閉じ、次の表とページへ進める。一描画単独で超過する場合は native Write 前に拒否する。全 materialize 成功後に参照を解決し、最後に payload/record を pack する。upload 済みの wire を別 table へ root の変更だけで rebase しない。

固定長 storage の統合と logical batch の統合は別である。Graph は既存の一 pass 宣言全体を容量単位とし、組込み処理が事前に pass を分割する。手動 AddPass の宣言超過を自動分割しない。record buffer、paint 評価命令の buffer、native command buffer、graph の pass 登録表を別の所有単位として扱う。間接到達する資源も各 pass に標準 Use 宣言を行い、copy-write producer と shader-read、culling、barrier を省略しない。

### 所有権と retained 更新

準備中の CPU geometry/mask cache と pack plan は準備限定の所有者に置き、upload または最後の遅延 pack 後に mask 配列と辞書容量を解放する。失敗、取消、append 中断、graph abandon/culling でも解放する。小さな生存 retained asset が無関係な大きい mask を pin しないことを維持する。

完成ページは immutable snapshot とし、table と参照先資産を含む shared owner/refcount を持つ。GPU 完了 **かつ command disposal** 前には上書き・pool 返却しない。借用画像/view/sampler の所有者は従来どおり呼出し側であり、shared owner は内部で生成した資源の所有と借用参照の保持を区別し、borrowed 資源の Dispose を防ぐ新しい lease 保証は加えない。retained の部分更新はページ単位 copy-on-write とする。未変更ページは共有できるが、同ページの生存 record の再 pack、挿入・削除・並べ替えによる後続区間の再構築は起こり得る。登録表の所属が変わるページは関連 payload も再 pack する。毎 frame が O(dirty) になる保証や framebuffer damage redraw は追加しない。retained 予算には shared bytes の重複を避けつつ、CPU 保持分、GPU bytes、古い in-flight snapshot をすべて含める。MaxCachedBytes の既定値と設定値は維持し、idle cache の受け入れ時には active snapshot の保有分も予算を予約する。active frame を eviction したり、既に受理した frame を遡って拒否したりせず、非解放可能な保有分が予算を超える間は idle を保持しない。キーが保持する不変 CPU page、staging/device buffer と共有登録 plan は owner identity ごとに保守的に計数する。driver の内部 heap 消費量を推測して正確な VRAM 使用量とは呼ばない。

### 内部 API 差分

以下は追加する宣言の抜粋で、namespace は既存 Drawing2D と同じにする。既存 DrawingArguments は graph 合成用として維持し、汎用 command の生成だけを新引数へ移す。IRenderEncoder.SetArguments/Draw と IShaderData の公開契約は変えない。DrawingRetentionOptions2D.MaxCachedBytes の説明は次の差分とし、frame の受理上限と caller-managed な寿命保証は維持する。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
     public sealed record DrawingRetentionOptions2D
     {
-        // idle reusable batch の推定 bytes の上限。
+        // 共有 owner と active snapshot の保有分も予約する retained-memory 予算。
+        // active frame は eviction せず、遡って拒否しない。既定値は維持する。
         public long MaxCachedBytes { get; init; } = 64 * 1024 * 1024;
     }
 }
```

各 buffer の IGpuRef は通常の generated codec と選択済み table を使い、追加の公開 binding API を作らない。

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
+        Vector2 TargetSize, Vector2 PixelOrigin,
+        uint Reserved0, uint Reserved1) : IShaderArguments;
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
+    // uint 群と typed 参照の正確な lane は「全 stream の byte 配置」に固定。
+    internal readonly partial record struct ImagePaintData2D(
+        Vector4 InverseX, Vector4 InverseY, Vector4 Source, Vector4 TextureSizeAndRange,
+        uint ChildId, uint TileX, uint TileY, uint Filter,
+        uint Alpha, uint FieldMode, uint Reserved0, uint Reserved1,
+        IGpuRef<IGraphicsTextureView> Texture,
+        IGpuRef<IGraphicsSampler> Sampler) : IShaderData;
 }
```

NumericPayload は geometry、paint 数値、clip 数値のページ内共通 16 B stream とする。directory の uint lane は uint4 wire を用い、NumericPayload 内で整数を扱う lane は bit-preserving な asuint/asfloat で読み書きし、float として数値 decode しない。mask の membership lane だけは既存の数値 float 方式を維持する。すべての reserved と未使用 lane を 0 にする。参照付き型も「全 stream の byte 配置」の固定 offset/stride を用い、全 backend の reflection と生成 codec で照合する。照合不一致は導入条件の不成立として拒否する。

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
- backend reflection と generated serialization の全表の offsets/stride を検査する。全 uint bit pattern（NaN 相当を含む）の NumericPayload 往復、空 stream の dummy/fallback と transitive binding、7+1 の exact/+1 binding limit、80/128 B descriptor の offset16 と LCM alignment を含める。32 KiB の 0/1/340/341/342 record、first/last/空/範囲外/uint overflow、256 B binding alignment と 96 B stride、typed offset、buffer/range の上限と tail 初期化を検査する。
- instance の透明重なり・quad 共通辺・混在 Kind と record ごとの同順 Draw を比較する。Vulkan、D3D12、Wgpu/Browser の API 順序保証の根拠確認と実機試験を分け、未確認 backend を完了扱いにしない。
- disjoint half の shape/clip の積が 0、coincident half が 1/2 となること、gradient 相関、16 bit の各 lane、穴、DPR/origin、隣接 float 境界、DrawMesh の中心 coverage を独立 oracle で確認する。
- table exact/+1 と単一超過、transitive reference、paint sharing と別 table 再 pack、AddPass 超過拒否、組込み pass 分割、copy producer と culling を検査する。
- retained の旧 snapshot と複数 in-flight、挿入/削除/並べ替え、cache eviction、CPU/GPU byte accounting、upload failure/取消/graph abandon、完了前返却拒否と cleanup retry を構造的に検査する。

採用は上記の導入条件を満たした実装や backend reflection、pixel、性能の検証済みを意味しない。導入時には各受け入れ条件を別途確認する。
