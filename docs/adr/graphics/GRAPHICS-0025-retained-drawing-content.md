# ADR-GRAPHICS-0025: 描画内容を保持し、内容と配置の変更を分けて更新する

- 状態: 提案
- 日付: 2026-11-03
- 基準コミット: `fbc81051cbf0f4aa5d996237543765e02b8cfa5c`

## 背景

同じ図形を表示し続ける利用者も、位置を変えて描くたびに全描画命令を記録し直している。基準コードの `DrawingCanvas2D.Replay` は命令を一件ずつ追加し、`DrawList2D` の生成と `Renderer2D.PrepareAsync` も全命令を走査するため、変わっていない図形にも処理費用がかかる。

基準コードは `src/Graphics/Lumyte.Graphics.Drawing2D/` の `Model/DrawingCanvas2D.cs`、`Model/DrawList2D.cs`、`Execution/Renderer2D.cs`。UI は既に要素の識別、部分木の更新、レイアウト結果の保持を行っており、その変更情報を描画にも利用できる。

## 決定

描画内容を一度登録して保持し、内容の変更と位置・順序の変更を別々に通知できる仕組みを提案する。変更されていない内容と準備済みの GPU 資源を再利用し、利用者が毎フレーム全命令を組み立て直す必要を減らす。既存の Canvas と DrawList の経路は維持し、移行は任意とする。

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

変更後の提案では、最初に内容と配置を登録し、返された ID を保持する。

```csharp
using var scene = new DrawingScene2D();
DrawingNodeId2D node;
using (var update = scene.BeginUpdate())
{
    node = update.Insert(scene.Root, 0, content);
    update.SetTransform(node, transform);
    using var initial = update.Commit();
}
```

位置だけが変わったときは、その配置だけを更新する。内容が変わった場合は `ReplaceContent` を使う。

```csharp
using (var update = scene.BeginUpdate())
{
    update.SetTransform(node, nextTransform);
    using var changed = update.Commit();
}

// 変更がないフレームでは更新処理は不要。描画側は確定した版を取得する。
using DrawingSnapshot2D snapshot = scene.Capture();
```

ここでは CPU 側の記録更新だけを示し、元の `content` の作成、デバイスの初期化、GPU への描画を省略している。描画側は取得した版を `Renderer2D.PrepareAsync` に渡し、毎フレーム新しい `PreparedDrawingFrame2D` を受け取る。GPU 使用中の資源を即座に解放できるわけではなく、以下の寿命管理が必要になる。

### 描画内容と配置

保持する描画内容を「断片」と呼ぶ。断片は独立して再記録できる不変の命令集合であり、同じ断片を複数箇所へ配置できる。内容の識別子と配置の ID は分け、配置の ID にはシーンと世代を含める。別シーン、削除済み、再利用前の ID は拒否し、配列の添字を永続 ID にしない。

内容、配置の属性、親子関係・順序、外部資源の変更は別々の版番号で追跡する。一つの変更フラグで全ての保持内容を無効にしない。ただし、大きな断片の一命令だけを変えても、その断片全体の置換になる場合がある。

親自身の描画と子の参照を交互に記録できる順序を保つ。透明描画を型や塗りの種類で全体並べ替えせず、断片内で捕捉したクリップ座標と外側のクリップを区別する。子の配置を変えても親のクリップは動かさない。断片と GPU 上の格納・描画単位は分け、一つの UI 要素につき一回の描画へ固定しない。

### 更新の確定と CPU 側の所有権

更新は一度に一つだけ開始できる。変更する部分とその祖先を複製し、検証に成功した `Commit` で新しい版を一括公開する。失敗、取消、未確定の `Dispose` では旧版を維持する。変更のない `Commit` は同じルートと内容の版番号を返す。

`Capture` は確定済みの版への参照を取得するだけとし、全命令の再記録・連結・ハッシュ計算をしない。取得した版は後の編集や削除で変わらず、ID も取得時の版で解決する。取得、更新確定、破棄の競合によって使用中の版が解放されないよう同期する。

CPU 配列は GC 所有でよい。Canvas が専有するバッファは移譲できるが、外部から受け取る可変データはコピーまたは明示的な所有権移譲が必要になる。読み取り専用の包みだけでは不変性を保証できず、参照中の配列をプールへ返してはならない。

### 再利用する資源とフレームごとの寿命

保持する期間を次の三つに分ける。

| 対象 | 保持するものと再利用条件 |
| --- | --- |
| `DrawingSnapshot2D` | CPU の記録と外部資源への参照。後の編集から独立した版として保持する |
| 準備済み描画資源 | 形状・塗りの解析結果と GPU データ。デバイス、実行環境、シェーダー、引数の結び付け、参照先資源の世代が一致するとき再利用する |
| `PreparedDrawingFrame2D` | 今回の描画先、資源の割当・転送、準備済み資源の使用権。命令を記録するのは一回だけ |

再利用の判定には形状、塗り、クリップ座標、画面倍率、近似精度などの依存も含める。CPU 配列でもデバイス固有の資源参照を含む場合はデバイス依存であるため、解析結果とデバイス固有の参照を作る処理を分ける。使用済みの `PreparedDraw2D`、コマンドバッファ、前フレームだけで有効な RenderGraph のハンドルは保持して再実行しない。

借用したテクスチャとビューは、CPU から参照しているだけでは `Dispose` を防げない。呼び出し側は、取得済みの版を再度使わなくなり、その全 GPU 使用が終わるまで資源を生存させる。デバイスを失った場合は参照を解決し直すか、新しいビューへ置換する。Resources との接続には既存の公開版と使用権を使い、基本 Drawing2D から ResourceManager への依存は追加しない。

GPU が使用中の格納領域は上書きせず、変更分を別領域に置き、最後のフレーム使用まで旧領域を保持する。キャッシュからの除外も使用中資源の即時解放を意味しない。保持量と同時フレーム数に上限を設け、満杯のときは追加処理を制限する。

### GPU 完了後の解放

`PreparedDrawingFrame2D` はコマンドと送信結果を借用し、送信・待機・破棄を代理しない。呼び出し側は次の順序を守る。

1. `Record` を一回呼び、コマンドを送信する。
2. 実際に送信が返した `IGraphicsSubmission` を `AttachSubmission` へ渡す。
3. GPU 完了を確認してコマンドを破棄し、`Retire` を呼ぶ。
4. 送信結果とフレーム所有者を破棄する。

`Retire` は送信結果が `Completed`、コマンドが `Disposed`、デバイスが有効であることを確認し、再利用可能な GPU 上の版を公開する。転送命令の記録だけでは公開しない。送信結果とコマンドの正しい対応付けは呼び出し側の責務である。

送信を試していない場合に限り、先にコマンドを破棄してから `AbortUnsubmitted` で使用権を返せる。送信の成否が不明な場合や待機を取り消した場合は、解放・再利用を進めない。`Dispose` も未完了の GPU 使用の解放を代行しない。

### UI と独自描画への適用

UI の要素 ID と確定したレイアウトを配置へ対応付ける。位置だけの変化は配置を更新し、寸法に依存する描画は内容を更新する。`RecordSelf` と `RecordChild` の順序を保ち、別の UI ツリーを追加しない。

独自の `OnRecord` は、再利用条件または明示的な無効化が提供されなければ毎フレーム記録する。独自の描画処理も、再利用への明示的な参加と版番号の契約がなければ毎フレーム準備する。描画処理の選択は標準表の `CreateDefault` または既存表を使う `FromLegacy` で明示し、未対応処理を標準処理へ黙って置き換えない。

`UiVisualLayer` と画面遷移も同じ接続を使い、画像化したレイヤー、共有要素の除外と後段合成を維持する。時刻・アニメーション・描画提示の結果・遷移完了通知は既存の更新と提示の境界で進め、再記録の有無に依存させない。レイアウト、スケジューラー、描画先の能力確認、GPU 待機の費用は別に残る。

### 公開 API 差分

基準コミットに対する追加案を示す。型名と公開範囲も提案であり、以下の API は基準時点には存在しない。既存の `Renderer2D` には取得済みの版を受け取るオーバーロードを追加する。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
+    // 不変なCPU内容。新たな内容の構築APIはこの判断の対象外。
+    public sealed class DrawingContent2D;
+    public sealed class DrawingContentRenderers2D
+    {
+        public static DrawingContentRenderers2D CreateDefault();
+        public static DrawingContentRenderers2D FromLegacy(DrawingCommandRenderers2D renderers);
+    }
+    // 解放順と失敗時の扱いは「GPU 完了後の解放」に従う。
+    public sealed class PreparedDrawingFrame2D : IDisposable
+    {
+        public void Record(IGraphicsCommandBuffer commands);
+        public void AttachSubmission(IGraphicsSubmission submission);
+        public void Retire();
+        public void AbortUnsubmitted();
+        public void Dispose();
+    }
+    // シーンと世代を持つ。default、失効済み、別シーンの値は拒否する。
+    public readonly struct DrawingNodeId2D;
+    public sealed class DrawingScene2D : IDisposable
+    {
+        public DrawingScene2D();
+        public DrawingNodeId2D Root { get; }
+        public DrawingSceneUpdate2D BeginUpdate(); // 更新は一度に一つ。
+        public DrawingSnapshot2D Capture(); // 確定した版を取得する。
+        public void Dispose(); // 新規取得を止め、取得済みの版は維持する。
+    }
+    public sealed class DrawingSceneUpdate2D : IDisposable
+    {
+        public DrawingNodeId2D AddGroup(DrawingNodeId2D parent, int index);
+        public DrawingNodeId2D Insert(DrawingNodeId2D parent, int index, DrawList2D content);
+        public void ReplaceContent(DrawingNodeId2D node, DrawList2D content);
+        public DrawingNodeId2D Insert(DrawingNodeId2D parent, int index, DrawingContent2D content);
+        public void ReplaceContent(DrawingNodeId2D node, DrawingContent2D content);
+        public void SetTransform(DrawingNodeId2D node, System.Numerics.Matrix3x2 transform);
+        public void SetClip(DrawingNodeId2D node, Path2D? clip, FillRule2D rule = FillRule2D.NonZero);
+        public void Move(DrawingNodeId2D node, DrawingNodeId2D parent, int index);
+        public void Remove(DrawingNodeId2D node);
+        public DrawingSnapshot2D Commit(); // 検証後に一括公開。失敗時は旧版を維持する。
+        public void Dispose(); // 未確定の変更を取り消す。
+    }
+    public sealed class DrawingSnapshot2D : IDisposable
+    {
+        public ulong Revision { get; }
+        public void Dispose();
+    }
     public sealed class Renderer2D
     {
+        // フレームごとに新しい所有者を返す。再利用する資源とは寿命を分ける。
+        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D snapshot,
+            RenderTarget2D target, DrawingContentRenderers2D renderers,
+            CancellationToken cancellationToken = default);
     }
 }
```

## 検討した代替案

- 記録バッファの所有権を移すだけにする: コピーを減らせるが、毎フレームの全記録・全準備が残る。
- 全再記録の後に差分を取る: 比較方法を単純にできるが、比較前の構築費用が残り、UI の変更通知を活用できない。
- 一つの可変配列を直接書き換える: 格納は単純になるが、取得済みの版と GPU 使用を壊し、挿入や並べ替えで全体移動が生じる。
- 描画基盤全体を置き換える: 保持構造を一から統一できるが、既存のバックエンド、描画範囲、拡張契約の互換性まで変更対象になる。本提案は既存経路へ追加する。

## 結果と影響

利用者は、位置だけの変更で描画内容を再記録する処理を省ける。静止・局所更新では準備費用も減らせる可能性がある一方、配置 ID の保持、更新の確定、GPU 完了後の解放が新たな責務になる。内部にも版管理、旧領域の保持、キャッシュ管理の費用と常駐メモリーが増える。

親の属性を変えると子の配置を再計算する場合があり、GPU 命令の記録と描画は可視要素数に比例する場合がある。フレーム全体の費用が変更量だけに比例するとは保証せず、一般の図形・文字・UI に対する速度向上もこの提案だけでは確定しない。

### 検証する条件と対象外

全再記録と画素を比較し、半透明、クリップ、並べ替え、同じ内容の複数配置で結果が一致することを確認する。取得済みの旧版、失効 ID、更新失敗、変更なしの確定も検証する。

3 フレームを同時処理する条件で、再利用する格納領域が途中の版を飛ばしても正しい内容になることを確認する。未送信の転送、待機取消、デバイス喪失、資源更新、保持量超過、再利用に参加しない独自描画も対象にする。構築・確定・取得・準備・転送データ作成・転送・命令記録・送信を分けて、時間、割当量、保持量、描画回数、解放待ちを測る。

初期の描画は描画先全体を消去して全要素を描いてよい。変更領域だけの描画・提示、一括更新用の構築 API と転送選択、新しい UI 基盤や資源管理系は対象外とする。独自描画の再利用インターフェース、RenderGraph のレイヤー・効果の編集、GPU 格納単位と予算、UI で再利用を有効にする範囲は未決定である。
