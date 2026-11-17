# ADR-GRAPHICS-0042: 形状の相関 sample 判定を compute と raster に分ける

- 状態: 採用
- 日付: 2026-11-18
- 基準コミット: `ab480b6480bf0cb233a60bd29f404e826ed1cf0f`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0041 の共有する内容と更新](GRAPHICS-0041-immutable-gpu-drawing-retention.md#共有する内容と更新)
- 変更対象: 明示した方式の path 判定に準備単位の可変 GPU buffer を追加する。
- 継続する判断: 11 Slot、配置ごとの processor、CPU scene、完了確認と資源回収を維持する。

## 背景

path の塗りと stroke を描く利用者は、形状の内外判定を塗りの評価とは別の compute pass に置きたい。一方、形状と塗りと clip の被覆率を別々に平均すると同じ pixel の描画結果が変わるため、全 16 sample の対応を保つ必要がある。

基準コミット ab480b6480bf0cb233a60bd29f404e826ed1cf0f の src/Graphics/Lumyte.Graphics.Drawing2D/Pipelines/DrawingCompose.Standard.cs は命令別 Default processor を生成する。同じ SHA の Rendering/DrawRecordPage2D.cs と Rendering/PreparedRecordBatch2D.cs は CPU の命令を固定 record に変換し、Execution/Renderer2D.cs は一つの資源所有者と準備ごとの引数計画で通常の描画を実行する。

## 決定

CoverageMembership の命令別 factory を追加し、path と stroke の 4×4 内外判定を compute で生成してから通常の raster paint、clip、SrcOver 合成を実行する。既存の Standard factory は従来の Default processor を維持し、利用者が Slot ごとに新方式と明示的な代替候補を選ぶ。

### 利用者の変更前・変更後

変更前の完全な 11 Slot 宣言では、たとえば path に次を指定する。

~~~csharp
Renderer.FillPath()[StandardFillPath()]
~~~

変更後は同じ Root と BuildRenderer を使用する。以下は全命令を明示した例であり、device、resources、resourceOwner、target と list の作成、記録後の submission と完了処理は省略している。

~~~csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

using RendererBuildHost2D host = Renderer(
    argumentTables: DrawingArgumentTableMode.Auto,
    retention: RetentionLimits(maxFramesInFlight: 2, maxCachedAssets: 512,
        maxCachedBytes: 64 * 1024 * 1024))
[
    Renderer.FillRect()[CoverageMembershipFillRect(), StandardFillRect()],
    Renderer.FillRoundedRect()[CoverageMembershipFillRoundedRect(), StandardFillRoundedRect()],
    Renderer.FillCircle()[CoverageMembershipFillCircle(), StandardFillCircle()],
    Renderer.DrawLine()[CoverageMembershipDrawLine(), StandardDrawLine()],
    Renderer.FillPath()[CoverageMembershipFillPath(), StandardFillPath()],
    Renderer.StrokePath()[CoverageMembershipStrokePath(), StandardStrokePath()],
    Renderer.DrawImage()[CoverageMembershipDrawImage(), StandardDrawImage()],
    Renderer.DrawCoverageMask()[CoverageMembershipDrawCoverageMask(), StandardDrawCoverageMask()],
    Renderer.DrawDistanceField()[CoverageMembershipDrawDistanceField(), StandardDrawDistanceField()],
    Renderer.DrawMesh()[CoverageMembershipDrawMesh(), StandardDrawMesh()],
    Renderer.DrawPaintGraph()[CoverageMembershipDrawPaintGraph(), StandardDrawPaintGraph()]
].CreateHost();
using IRenderer2D renderer = host.BuildRenderer(device, resources, resourceOwner, null);
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, target);
prepared.Record(commands);
~~~

最初に受理した同じ命令種の候補だけを一度準備する。候補の実際の確保・shader・記録の失敗を捕捉して次候補へ描き直さない。全候補が拒否した場合は NotSupportedException とする。

### 対応する入力と判定

11 factory は対応する typed Slot の能力を宣言する。FillPath、StrokePath と展開した DrawLine は固定 record の path・stroke であれば compute membership を使う。FillRect、FillRoundedRect、FillCircle、DrawImage、DrawCoverageMask、DrawDistanceField、DrawMesh、DrawPaintGraph は同じ固定 record の raster 評価を使い、user mesh の補間 coverage は二値化しない。path 以外の単純形状には compute pass を追加しない。

準備前の admission は CPU の形状・塗り・clip lowering、buffer 容量、descriptor 数、引数表の slot span、保守的な作業容量を検査する。compute を使う命令は 16×16 workgroup、256 invocation、dispatch 軸上限と整数の非負 pixel bounds を必要とする。custom shader の資源を組込み shader へ黙って置き換えず、非対応の候補として明示した Standard 候補へ選択を委ねる。Root の MaxWorkingBytes と先行する別命令 group の所有容量は、direct、保持型、親 Graph の各準備へ引き継ぐ。同じ引数計画の容量を二重に加算しない。非予算の親 context から OtherWorkingBytes を読み出して複製せず、方式の fork は共通の lazy な計上状態と service を引き継ぎ、予算を必要とするときに先行 group の値を一度解決する。通常の非予算経路は追加の辞書や prefix 走査を作らない。元の引数計画の準備 scope が成功・失敗のどちらで終わっても、別の引数計画を使う fork を含めて先行 owner を捕捉した callback を解除し、既に解決した容量の scalar だけを保持する。membership mask と placeholder descriptor を含む全 binding 数は、確保前の登録見積りと遅延 upload の不変な見積り値の両方へ渡す。page の事前計上には record、paint/clip directory、各 descriptor、metadata、numeric の data/staging と warm 貸出しの最大容量、membership と登録管理を含める。最後の custom group の所有容量も upload・map の前に合計し、direct の引数表を materialize した後にも集計を確認してから prepared owner を返す。

### 記録と資源の寿命

membership の uint は pixel ごとに 16 bit の内外判定を保持する。fragment はこの bit 列を同じ順の clip と塗り sample に掛け、最後に 16 で割る。stroke 判定は compute と fragment で共通の明示的 FMA を使う。

copy の後、record と numeric を compute read へ遷移し、membership output を shader write する。compute の終了後、output を fragment read、record と numeric を raster read へ遷移する。通常の prepared batch は active render pass の外から記録し、親 Graph は一つの prepared group の pass に copy、compute、raster の全資源アクセスを宣言する。group 内の barrier と encoder は通常の Record が管理し、pass 固有の可変 buffer を別 pass や frame へ公開しない。全 scene を先行描画せず、連続する型付き group の描画順を維持する。

membership 専用 page metadata は 96 byte、既存 record は 96 byte、directory は 16 byte、root は 64 byte を維持する。内部 variant 8–10 は membership の全 metadata と小・中の paint evaluator を使い分けるが、受理する paint closure と sample 数を減らさない。

同じ build の互換な意味設定は shader、reflection と pipeline の内部 service を共有する。同じ宣言を複数位置へ置いても processor は別 instance とし、Root の資源所有者、保持 cache と準備ごとの引数計画を重複して作らない。可変の membership は各準備 group に属し、二つの進行中 frame 間でも共有しない。resident range としての記録は禁止し、未完了 producer を cache hit だけで初期化済みとみなさない。

### 公開 API 差分

namespace Lumyte.Graphics.Drawing2D.Pipelines の DrawingCompose に、以下の factory と対応する Definitions の passive declaration を追加する。いずれも [Composable] の生成対象であり、CreateElement は独立した PipelineElement2D を生成する。

~~~diff
 public static partial class DrawingCompose
 {
+    public static Definitions.CoverageMembershipFillRect CoverageMembershipFillRect();
+    public static Definitions.CoverageMembershipFillRoundedRect CoverageMembershipFillRoundedRect();
+    public static Definitions.CoverageMembershipFillCircle CoverageMembershipFillCircle();
+    public static Definitions.CoverageMembershipDrawLine CoverageMembershipDrawLine();
+    public static Definitions.CoverageMembershipFillPath CoverageMembershipFillPath();
+    public static Definitions.CoverageMembershipStrokePath CoverageMembershipStrokePath();
+    public static Definitions.CoverageMembershipDrawImage CoverageMembershipDrawImage();
+    public static Definitions.CoverageMembershipDrawCoverageMask CoverageMembershipDrawCoverageMask();
+    public static Definitions.CoverageMembershipDrawDistanceField CoverageMembershipDrawDistanceField();
+    public static Definitions.CoverageMembershipDrawMesh CoverageMembershipDrawMesh();
+    public static Definitions.CoverageMembershipDrawPaintGraph CoverageMembershipDrawPaintGraph();
 }
~~~

IRenderer2D の Device、統計、3 種類の PrepareAsync、Renderer2D の既存 constructor、Canvas と prepared owner は変更しない。

## 検討した代替案

- Standard factory を新方式へ変更する: 呼出し側の宣言は短いが、従来の processor と直接 batching の意味を変更するため採用しない。
- pixel ごとの平均 coverage だけを compute で返す: buffer は単純になるが、塗りと clip の相関が失われるため採用しない。
- Slot ごとに Renderer2D を作る: 同じ Root の資源、引数計画と寿命を重複させるため、配置 processor と build-local service に分離する。

## 結果と影響

利用者は通常の Canvas と renderer のまま、命令別に形状判定方式を選択できる。compute dispatch と準備単位の可変 buffer が増え、対応能力と作業容量の検査が必要になる。dispatch 数は実際の path record 数と分割した page 数に依存し、常に scene 全体で一回とは保証しない。

保持型の経路ではこの方式の準備結果を読み取り専用の resident geometry として扱わない。CPU scene は正本のままとし、通常の完了・command 破棄・退役と回収失敗の再試行責任を維持する。別の描画方式、全 scene の executor、性能保証は対象外である。
