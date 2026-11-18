# ADR-GRAPHICS-0045: 描画レコードから Mesh ステージで被覆領域を生成する

- 状態: 採用
- 日付: 2026-11-18
- 基準コミット: `ab480b6480bf0cb233a60bd29f404e826ed1cf0f`

## 背景

2D renderer の作者は、描画の形状と塗りを保ったまま Mesh ステージを選択したい。低レベルの Mesh pipeline だけでは、既存の描画レコード、引数表、相関した被覆評価を接続できない。

基準コミットの src/Graphics/Lumyte.Graphics.Drawing2D/Rendering/PreparedRecordBatch2D.cs は 96 byte の record と数値列を GPU に転送し、Vertex ステージで矩形領域を生成する。[GRAPHICS-0038 のプログラム、状態と命令](GRAPHICS-0038-mesh-shader-pipelines.md#プログラム状態と命令) は Task なしの Mesh pipeline と DrawMeshTasks を提供する。この低レベル契約を変更せず、2D の命令別候補として追加する。

## 決定

Mesh coverage factory は実際の record bounds から四頂点・二三角形を生成する Mesh ステージを使う。既存の Fragment evaluator が形状、paint、clip の相関した 4×4 sample と premultiplied display-linear 色を評価し、Canvas と準備結果の公開型は維持する。

### 利用者の変更前・変更後

変更前は全 Slot に Standard factory を指定する。変更後は対応する十種類の Slot に Mesh factory と必要な容量を指定し、indexed mesh は明示した StandardDrawMesh に残す。次の抜粋は同じ FillRect Slot の置換であり、残り十 Slot と host、device、target、完了確認の手順は基準コードの構築契約を維持する。

~~~csharp
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

Renderer.FillRect()[StandardFillRect()];
// 変更後。非対応環境では明示した第二候補を使う。
Renderer.FillRect()[
    MeshFillRect(options: MeshCoverageLimits(maxCommands: 256,
        maxWorkingBytes: 16 * 1024 * 1024, numericPoolBytes: 1024 * 1024)),
    StandardFillRect()
];
~~~

既存 StandardFillRect の意味は変更しない。Mesh だけの Slot は非対応の admission を false とし、全候補が false なら準備を拒否する。GPU 作成時の例外を捕捉した再実行は行わない。

### 対応範囲と容量

FillRect、FillRoundedRect、FillCircle、DrawLine、FillPath、StrokePath、DrawImage、DrawCoverageMask、DrawDistanceField、DrawPaintGraph を個別 factory とする。DrawMesh は頂点ごとの intrinsic coverage と indexed triangle の意味を持つため、この被覆矩形方式の対応対象とせず、StandardDrawMesh を明示する。

Mesh feature と非 null の MeshShaderLimits を両方要求する。実際の target artifact の reflection から local size、output vertex・primitive 数、shared・payload・output byte 数と合計上限を照合し、準備の GPU allocation より前に拒否する。Task は使わない。WGSL、Browser、現在の Wgpu は非対応とする。DXIL の offline compile と D3D12 の実機実行は別の受け入れ条件である。

作業量制限を要求する処理だけが累積会計を有効にし、従来処理だけの frame では追加の所有者走査と参照予約辞書を作らない。context の派生は準備内で共有する無効化可能な会計元を保持し、先行する従来 page の参照数は upload に保持した不変の見積値から取り込む。準備元の引数計画が会計 callback の寿命を持ち、成功・失敗とも戻り値の公開前に捕捉した frame 所有者を解放する。評価済みの容量値だけは後続の記録と回収のために保持する。

作業量の上限は MeshCoverageLimits と root の保持方針の小さい方とし、Direct と通常の親 Graph の両方で、それ以前の連続 group が所有する資源も含めて判定する。同じ引数計画は重複計上せず、次の group の GPU buffer 確保より前に残り容量を確認する。record、paint directory と数値列に加え、引数表の buffer・view・sampler 参照世代も収集段階で予約し、表の実体化後にだけ増える未計上分を残さない。確保後と payload の最終化時にも以前の group を含む完全な合計を、加算の overflow を起こさず再検査する。

一つの workgroup が一つの record を生成し、各 record の DrawMeshTasks(1) を painter order で記録する。dispatch をまたいだ順序、既存の target Load、blend、clip、DPR と平行移動の 8 byte property を保つ。Copy からの可視化には MeshShader と FragmentShader を使う。

### 所有権と API 差分

同じ build の意味的に等しい immutable options は内部 service を共有する。各配置の processor と各 frame の引数計画は独立する。service の pipeline と shader は準備済み結果が残る間保持し、renderer の破棄後にも記録できる。入力 texture、sampler と glyph は借用し、回収に失敗した所有者の再試行経路を維持する。

保持方針を指定した場合、内部の Mesh record owner だけを不変 payload として識別する。任意の custom callback の retained interface や公開 flag は適格性を与えない。cache は一つの構築済み renderer に閉じ、固定された Slot、service、options と device 世代を越えて共有しない。従来の完了確認と先行 producer の証明を変えず、可変の root property は毎回記録する。平行移動の最適化は全候補が内部 Mesh processor の Slot に限り、custom または Standard を含む候補列では従来どおり再構築する。

保持する連続命令では、各 Slot が単一の内部 Mesh 候補で、構築内の同一 service を参照し、合計命令数が全体の上限内に収まる場合だけ、共通の不変 record owner にまとめる。異なる options・service、Standard、custom と複数候補列では従来の型別 admission と候補順序を維持し、容量超過を分割で迂回しない。

追加宣言は Lumyte.Graphics.Drawing2D.Pipelines に置く。既存 IRenderer2D、Canvas と PrepareAsync の戻り型は変更しない。

~~~diff
 namespace Lumyte.Graphics.Drawing2D.Pipelines;
+// 全値を明示し、正の命令数・作業量と非負の idle pool 容量を要求する。
+public sealed record MeshCoverageOptions2D(
+    int MaxCommands, long MaxWorkingBytes, long NumericPoolBytes);
+// DrawingCompose に容量の factory を追加し、DSL 内で new を必要としない。
+public static MeshCoverageOptions2D MeshCoverageLimits(
+    int maxCommands, long maxWorkingBytes, long numericPoolBytes);
+// DrawingCompose の生成 factory。対応する各命令にも同形を追加する。
+public static Definitions.MeshFillRect MeshFillRect(MeshCoverageOptions2D options);
~~~

## 検討した代替案

- Vertex 描画を Mesh という名前で包む: 既存経路を再利用できるが、Mesh ステージが geometry を生成しないため採用しない。
- 全命令を一つの renderer facade で置換する: 混在候補と命令別の明示選択を失うため採用しない。
- indexed mesh も被覆矩形に置換する: intrinsic coverage の補間を失うため、この方式では受け入れない。

## 結果と影響

利用者は対応する命令だけ Mesh を選択し、非対応環境への候補を構築構文で明示できる。数値評価の正本と描画モデルは既存のままであり、速度の向上は保証しない。

受け入れ条件は実際の DrawMeshTasks の記録、同一条件の従来描画との全画素比較、renderer 破棄後の記録、複数配置の service 共有、容量超過の早期拒否である。非対応環境の拒否検査と対応 GPU の全画素検査は分け、skip を実行成功として扱わない。
