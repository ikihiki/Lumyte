# ADR-GRAPHICS-0025: 描画内容を保持し、内容と配置の変更を分けて更新する

- 状態: 採用
- 日付: 2026-11-03
- 基準コミット: `fbc81051cbf0f4aa5d996237543765e02b8cfa5c`

## 背景

同じ図形を表示し続ける利用者も、位置を変えて描くたびに全描画命令を記録し直している。基準コードの `DrawingCanvas2D.Replay` は命令を一件ずつ追加し、`DrawList2D` の生成と `Renderer2D.PrepareAsync` も全命令を走査するため、変わっていない図形にも処理費用がかかる。

基準コードは `src/Graphics/Lumyte.Graphics.Drawing2D/` の `Model/DrawingCanvas2D.cs`、`Model/DrawList2D.cs`、`Execution/Renderer2D.cs`。UI は既に要素の識別、部分木の更新、レイアウト結果の保持を行っており、その変更情報を描画にも利用できる。

## 決定

配置は可変の木、描画内容は既存の不変な `DrawList2D`、GPU への実行順は順序付きの列として保持する。内容、配置の属性、親子関係・順序を分けて更新し、未変更の内容と準備済み GPU 資源を再利用する。利用者は毎フレーム全命令を組み立て直す代わりに変更した対象だけを通知でき、既存の Canvas と DrawList の経路も引き続き選べる。

### 利用者の変更前・変更後

例は「同じ描画内容を別の位置へ移す」操作である。`content` は作成済みの `DrawList2D`、`transform` は現在の配置を表す `Matrix3x2` とする。

変更前は、各フレームで配置を適用して全命令を再記録する。

```csharp
var canvas = new Canvas2D();
canvas.Begin();
canvas.ConcatTransform(transform);
canvas.Replay(content);
DrawList2D drawing = canvas.End();
```

変更後は、通常の Canvas で記録した `DrawList2D` をそのまま登録し、配置の ID を保持する。別の公開内容型への変換は不要である。

```csharp
using var scene = new DrawingScene2D();
DrawingNodeId2D node = scene.Insert(scene.Root, 0, content);
scene.SetTransform(node, transform);

// 位置だけが変わったときに呼ぶ。内容の再記録は不要。
scene.SetTransform(node, nextTransform);

// 借用中は scene の更新を止め、Prepare が返った後で借用を解放する。
PreparedDrawingFrame2D frame;
using (DrawingSceneReadLease2D read = scene.Borrow())
{
    frame = await renderer.PrepareAsync(read, target, cancellationToken);
}
```

内容が変わった場合は `ReplaceContent`、親子関係・順序が変わった場合は `Move` を使う。変更がないフレームでは更新操作は不要である。借用の取得だけで全描画内容を再記録・連結し直さない。

ここでは内容の作成、デバイス・描画先の初期化、送信前の描画先状態遷移を省略している。GPU に送信した `frame` の寿命は「GPU 完了後の解放」に従い、借用を解放しただけで GPU 資源を再利用しない。

### 木・ページ・実行順の役割

木の節は、独立して更新できる描画内容または子のまとまりを配置する単位である。各節の自身の内容を先に描き、次に子を格納順に描く。自身の描画と子を交互に配置したい場合は、それぞれの内容を順序付きの子として置く。透明描画を型や paint の種類で全体並べ替えしない。

`DrawList2D` が不変の命令を所有し、同じ記録を複数の配置・scene で共有できる。保持処理に必要な正規化済みページは `DrawList2D` 内で遅延生成して cache し、各配置や scene で変換し直さず共有する。ページは CPU の格納・共有に用いる内部実装であり、新しい公開概念や編集単位ではない。公開する命令数は `Count` だけとし、ページ列・容量・ページ数やページ／範囲の編集 API は公開しない。内部の `WithPage` はテスト・計測用の実装として残せるが、利用者は新たに記録した `DrawList2D` で内容を置換する。

ページは木の節や GPU の draw と一対一に対応しない。同じ内容ノード内の互換命令の連続範囲を batch として準備し、配列の都合だけで矩形一件またはページ一枚ごとの draw に分解しない。

ノードをまたぐ batch 統合はしない。局所更新の単位を小さなノードへ分けると再準備量を減らせる一方、draw 数と記録費用が増える。大きな DrawList2D を再記録して ReplaceContent するとページ参照がすべて変わるため、内部ページ置換の計測を公開 API の局所更新性能と同一視しない。実際の公開 API と UI adapter でこの境界を確認する。

配置の ID はシーンと世代を含む。別シーン、削除済み、再利用前の ID は拒否し、配列の添字を永続 ID にしない。親への移動では循環を拒否する。更新操作は検証後に反映し、失敗した操作で直前の状態を変更しない。

内容、配置の属性、親子関係・順序は別々に追跡する。順序だけの変更で内容と変換属性の版を進めず、準備済み内容を再利用する。親の transform や clip は子へ伝播するが、親が捕捉した clip を子の移動で動かさない。clip の捕捉座標と描画内容内の座標を区別する。

木の編集で影響を受けた配置だけを更新し、親子関係が変わった場合は実行順を作り直してよい。GPU 命令の記録と可視要素の走査は毎フレーム残り得る。ページ、実行順、GPU 格納領域の管理費用を無視して、frame 全体が変更件数だけに比例するとは保証しない。

### 最新状態の借用と任意の過去状態

通常の編集は最新の可変 scene に対して行う。`Borrow` が返す `DrawingSceneReadLease2D` の生存中は scene の更新を拒否する。複数の読取り借用は併存できる。非同期の `PrepareAsync` が借用を読み終わるまで呼出し側が借用を保持し、返却後は CPU scene を編集できる。

借用を解放した後の過去の CPU 状態は保証しない。過去の状態を後で描き直す必要がある利用者だけが `CaptureSnapshot` を呼び、配置の独立した版を明示的に保持する。内容とページは不変なので共有でき、通常の更新に常時 copy-on-write や過去状態の維持を課さない。scene の破棄は新規編集・新規借用を止め、既に取得した読取りと明示 snapshot の内容はその所有者の解放まで維持する。

CPU 配列は GC 所有とし、読取り中の配列をプールへ返さない。入力の `DrawList2D` は既存の不変記録を使用する。借用により得た可変 scene のメモリーを、借用解放後まで保持して読み続けてはならない。

### device 資産と frame の所有権

保持する期間を次の三つに分ける。

| 対象 | 保持するものと再利用条件 |
| --- | --- |
| `DrawList2D` | 不変な CPU 命令、内部の共有ページと外部資源への参照。scene の編集から独立して共有する |
| 準備済み描画資産 | 解析結果と GPU buffer・結び付け。device、描画先の形式・寸法・倍率、shader、属性・clip、外部資源の有効性が一致する場合に再利用する |
| `PreparedDrawingFrame2D` | 今回の描画先と資産の使用権。命令を記録するのは一回だけで、GPU 完了後に返却する |

GPU frame は準備時に必要な scene の識別・順序・属性と資産を捕捉し、返却された CPU 借用を後から読まない。GPU 使用中の buffer と引数の結び付けを上書きせず、必要な場合は別の資産を準備する。使用済みの `PreparedDraw2D`、コマンドバッファ、前フレームだけで有効な RenderGraph ハンドルを保持して再実行しない。

同時 frame 数と未使用 cache に上限を設ける。未使用の資産は予算内で保持し、除外時に解放する。使用中の資産を cache の都合で即時解放しない。上限に達した準備は明示的に失敗させ、呼出し側が既存 frame の完了・返却を進める。GPU 待機や送信は renderer が代理しない。

返却状態は各 frame の使用権に保持し、再利用される資産に古い返却状態を残さない。cleanup 失敗後に後続 frame が資産を借用しても、古い返却の再試行はその使用権に作用しない。GPU 完了または未送信が確定した枠は一度だけ返し、解放に失敗した資産は cache が保持して次の準備または renderer の破棄で再試行する。Prepare 途中の部分 batch もすべて cleanup を試み、失敗分の所有権と元の準備例外を失わない。

借用した texture と view は、CPU 参照だけで破棄を防げない。呼出し側は内容を再利用しなくなり、その全 GPU 使用が終わるまで生存させる。破棄・device 喪失・資源更新を検出した場合は古い結び付けを再使用せず、内容や renderer を有効な資源へ置換する。基本 Drawing2D から ResourceManager への依存は追加しない。

標準の描画処理を選択した命令は既存 fast path を維持する。独自 renderer は既存の登録表を通し、再利用の契約がなければ毎フレーム準備する。独自 renderer を迂回して標準 generic shader へ黙って置き換えない。

### GPU 完了後の解放

`PreparedDrawingFrame2D` はコマンドと送信結果を借用する。初期化済みの `commands`、`device` と前述の `frame` を使用する例を示す。記録中に失敗した場合は、送信を試さずコマンドを破棄して `AbortUnsubmitted` を使う。

```csharp
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

`Retire` は送信結果が `Completed`、コマンドが `Disposed`、device が有効であることを確認して、再利用できる GPU 上の版を公開する。転送の記録だけでは公開しない。実際の送信結果とコマンドの対応付けは呼出し側の責務である。

送信を試していない場合に限り、コマンドを破棄してから `AbortUnsubmitted` で使用権を返せる。送信の成否が不明な場合、待機の取消、未完了、device 喪失時は解放・再利用を進めない。`Dispose` は未記録の frame を破棄できるが、記録後の GPU 使用の解放や待機を代行しない。

### UI への接続

既存 UI の要素 ID と確定した layout を scene の配置へ対応付け、別の UI ツリーを導入しない。位置だけの変更は配置を、寸法や文字に依存する描画は該当内容を更新する。未変更の要素の内容を再記録しない。

独自の `OnRecord` は、再利用を明示的に選択し無効化を通知しなければ毎フレーム記録する。独自の `RecordOverride` が自身の描画と子の描画を交互に行う順序・状態も保つ。再利用の境界が分からない部分は既存の記録経路を使い、推測で分割・cache しない。

visual layer と画面遷移は既存の合成経路を保つ。時刻、animation、提示結果、遷移完了通知を、再記録の有無で省略しない。既存 `UiSurface.Record` と host の提示契約は維持する。

標準 Desktop／Browser Application は、通常 frame と補助 Window に Presentation ごとの `UiDrawingScene2D` を使用する。独自 host からも明示的に adapter を選択できる。通常の非活動 `UiVisualCoordinator` は分割保持を妨げず、Presence／page／未知の visual host／capture は既存の opaque 記録へ戻す。候補の GPU 検証と visual 画像の capture/update は direct のままとする。

全 surface の依存検証と layout が収束してから記録し、内容を共有する不変 snapshot に外側の scale → clip → translation を合成する。adapter 所有の root/topology を外部配置のために編集しない。短命の scene 借用は非同期処理の開始前に返し、snapshot は借用 texture の寿命を延ばさない。graphics 世代が変われば CPU 記録も再構築する。

標準 presenter は実際の submission を保持 frame へ結び付け、GPU と提示 image の完了後に command memory、保持 frame、submission の順で退役する。送信結果が不明な frame は隔離する。device 世代の退役を graphics service で確認した host に限り、内部の専用経路で command memory 解放後に使用権を返せる。この経路は residency を公開せず、通常の取消・device status だけを完了と見なさない。

### 公開 API 差分

基準コードへの追加を示す。既存 Canvas・DrawList・renderer table の利用は削除しない。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
     public sealed class Canvas2D
     {
         public DrawList2D End(); // 戻り値は引き続き不変の記録。
     }
     public sealed class DrawList2D
     {
+        public int Count { get; } // 記録済み命令数。ページの構造は公開しない。
     }
+    public readonly struct DrawingNodeId2D;
+    public sealed class DrawingScene2D : IDisposable
+    {
+        public DrawingNodeId2D Root { get; }
+        public DrawingNodeId2D AddGroup(DrawingNodeId2D parent, int index);
+        public DrawingNodeId2D Insert(DrawingNodeId2D parent, int index, DrawList2D content);
+        public void ReplaceContent(DrawingNodeId2D node, DrawList2D content);
+        public void SetTransform(DrawingNodeId2D node, System.Numerics.Matrix3x2 transform);
+        public void SetClip(DrawingNodeId2D node, Path2D? clip, FillRule2D rule = FillRule2D.NonZero);
+        public void Move(DrawingNodeId2D node, DrawingNodeId2D parent, int index);
+        public void Remove(DrawingNodeId2D node);
+        // 全借用の解放まで更新を拒否する。過去状態を保持する契約ではない。
+        public DrawingSceneReadLease2D Borrow();
+        // 配置を独立させた過去状態を明示取得する。
+        public DrawingSnapshot2D CaptureSnapshot();
+        public void Dispose();
+    }
+    public sealed class DrawingSceneReadLease2D : IDisposable;
+    public sealed class DrawingSnapshot2D : IDisposable;
+    public sealed record DrawingRetentionOptions2D
+    {
+        public int MaxFramesInFlight { get; init; } // 既定3。
+        public int MaxCachedAssets { get; init; } // 未使用資産、既定512。
+        public long MaxCachedBytes { get; init; } // 未使用資産の概算、既定64MiB。
+    }
+    // 記録と解放順は「GPU 完了後の解放」に従う。
+    public sealed class PreparedDrawingFrame2D : IDisposable
+    {
+        public void Record(IGraphicsCommandBuffer commands);
+        public void AttachSubmission(IGraphicsSubmission actualSubmit);
+        public void Retire();
+        public void AbortUnsubmitted();
+        public void Dispose();
+    }
     public sealed class Renderer2D
     {
+        // 既存の5引数 constructor を維持し、保持予算を指定する呼出しを追加する。
+        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
+            DrawingResources2D? resources = null, IDisposable? resourceOwner = null,
+            Action? validateResources = null, DrawingRetentionOptions2D? retentionOptions = null);
+        public DrawingRetentionStatistics2D RetentionStatistics { get; }
+        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
     }
 }
```

UI の明示的な接続には次の API を追加する。scene の編集と破棄は adapter が管理し、取得した借用・snapshot と GPU frame の解放は呼出し側が管理する。

```diff
+namespace Lumyte.UI
+{
+    public sealed class UiDrawingScene2D : IDisposable
+    {
+        public UiDrawingScene2D(UiSurface surface);
+        public DrawingScene2D Scene { get; } // adapter 所有、呼出し側から編集しない。
+        public void Update(); // layout と内容・配置を同期する。
+        public DrawingSceneReadLease2D Borrow(); // Update 後に読取りを借用する。
+        public void Dispose(); // surface は破棄しない。
+    }
+    public partial class UiElement
+    {
+        protected virtual bool CacheRetainedDrawing { get; } // 独自描画は既定 false。
+        protected void InvalidateDrawing();
+        protected virtual void ValidateRetainedDrawing();
+    }
+}
```

## 検討した代替案

- 記録と保持内容を別の公開型にする: 保持用の構造を型で区別できるが、同じ不変内容に変換と二つの概念を要求する。`DrawList2D` に内部ページを保持し、通常記録と scene で共有する。
- 全てを平坦な可変命令リストにする: 一括構築は単純だが、親の属性や部分木の移動を表す識別と更新境界が不足する。実行列には使い、論理構造には使わない。
- 全てを命令一件ごとの木にする: 更新対象を細かく表せるが、大量の同種データの格納・走査と batch 化に余分な間接参照を増やす。木の節は独立した配置単位にする。
- 全ての編集を不変 snapshot として公開する: 過去状態と編集を並行利用しやすいが、過去状態が不要な利用者にも所有権管理・複製の費用を課す。通常は最新状態を借用し、過去状態は明示取得する。
- 全再記録後に差分を取る: 比較前の構築費用が残り、既存 UI の変更通知を利用できない。内容と配置の更新を直接通知する。
- 画像に焼き付けて再利用する: 描画自体を減らせるが、解像度・拡大・描画先への依存が増える。今回は命令と準備済み資産を保持する。
- 描画基盤全体を置き換える: 既存 backend、描画範囲、拡張契約の互換性まで変更が広がる。既存経路へ任意に使える保持経路を追加する。

## 結果と影響

利用者は内容を一度記録して共有し、配置と順序の変更だけを通知できる。常時の過去 CPU 状態を必要としない用途では可変 scene を選び、必要な用途は snapshot を明示的に取得する。配置 ID の保持、借用中の編集停止、外部資源の生存、GPU 完了後の返却は利用者の責務になる。

資産の再利用で準備と割当を減らせる一方、cache の常駐量、木と実行順の更新、可視範囲の走査、命令記録と描画の費用が残る。親属性の変更で子の資産を再準備する場合があり、GPU property だけの更新や frame 全体の速度向上を保証しない。

### 受け入れ条件と対象外

既存経路と半透明・path・clip・画像・文字・並べ替え・同じ内容の複数配置を比較する。挿入・削除・再配置・親 clip の座標、失効 ID、不正更新の原子性、複数借用、明示 snapshot の旧状態、未変更時の内容再利用を検証する。同じ `DrawList2D` の複数配置・複数 scene で内部ページを共有し、元の記録と snapshot の内容を変更しないことも確認する。

複数 frame の同時処理では、使用中の資産を上書きしないこと、途中の更新を飛ばした frame でも内容が正しいことを確認する。未送信・完了前・失敗・取消・資源破棄・保持予算超過・再利用しない独自 renderer も検証する。変更なし・部分変更・全変更を分け、時間、割当量、転送量と常駐量を確認する。

変更領域だけの描画・提示、RenderGraph の layer/effect を保持して編集する機構、一括更新用の専用入力形式、新しい UI 基盤や資源管理系はこの判断の対象外とする。
