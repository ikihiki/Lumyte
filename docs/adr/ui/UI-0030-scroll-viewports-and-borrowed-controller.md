# ADR-UI-0030: 有限ビューポートと借用するスクロール位置

- 状態: 採用
- 日付: 2026-11-16
- 基準コミット: `9d9c3a06b68e9812ae48d815a1f8aeb557452d7b`

## 背景

一覧を構築する利用者は、有限の表示領域からはみ出す内容を移動し、入力位置と文字入力候補の位置を一致させる必要がある。[UI-0013](UI-0013-component-catalog-and-delivery-plan.md)のスクロール部品は候補であり、基準コードのStackには表示位置を操作する状態がない。

## 決定

UI本体に、一つの内容を持つScrollViewと借用可能なScrollControllerを追加する。ControlsのScrollBarは同じ位置を操作し、内容の所有権や文字編集の状態を持たない。位置を通常の子配置へ変換し、既存の描画、入力、意味情報、文字入力の座標計算を共有する。

### 利用者の変更前・変更後

変更前はStackの内容が表示領域を超えても移動できない。

```csharp
using static Lumyte.UI.Compose;
var content = Stack()[Button(height: 80), Button(height: 80)];
```

変更後は同じ内容を有限のScrollViewへ渡す。controllerは利用側が所有し、surfaceより長く保持する。宣言のsetterで位置を変更せず、controllerを明示的に操作する。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;
using var controller = new ScrollController();
using var surface = new UiSurface(ScrollView(controller: controller)[
    Stack()[Button(height: 80), Button(height: 80)]]);
surface.SetViewport(new(120, 100));
surface.UpdateLayout();
controller.ScrollTo(40);
surface.UpdateLayout();
```

### 配置と状態の所有

[UI-0003](UI-0003-layout-and-invalidation.md)の各子一回の配置を維持する。ScrollViewの両軸の最大制約は有限とし、スクロール軸だけ子の最大制約を無限にする。交差軸は有限に保つ。内容の確定サイズを後から変更せず、負の位置だけを子へ設定する。内容を省略した場合の範囲は0とする。

controllerを省略した場合はElementごとに位置を持つ。明示的なcontrollerは一つのviewportだけへ接続でき、同じ宣言を別surfaceへ接続する場合も二重接続を準備段階で拒否する。同じcontrollerを使う複数viewportの提案も公開前に拒否し、旧ツリーを保つ。接続は準備開始時に予約し、準備失敗では予約だけを解放する。公開時は利用側callbackより前に全接続を確定する。ScrollBarはviewportの所有者にはならない。

位置は有限かつ0以上、内容長から表示長を引いた上限以下へ制限する。表示長と内容長の変更でも範囲へ戻す。この配置内部の補正では利用側callbackを呼ばず、通常の位置操作で実際に値が変わった場合だけ通知する。位置変更は配置、入力情報、保持した描画を無効化する。controllerの操作は接続したスレッドで逐次行い、変更callbackより前に状態と無効化を完了する。callback例外は伝播し、変更済み位置は戻さない。切断は借用先を解放せず、controllerのDisposeは接続を解除する。

### 入力と表示領域への移動

wheelはsurface論理座標の位置と論理ピクセルの差分を持つ。各成分の有限性を入口で検査し、最内側から祖先へ軸別の残余だけを渡す。残余は変更callbackより前の受理差分で確定し、callbackの別操作を含めない。端で消費しなかった差分を二重適用しない。

touchはmouseおよびpenと区別する。移動のしきい値と主軸判定を超えるまで通常の子操作を継続し、スクロール開始時に子のcaptureを取消してviewportへ移す。文字編集およびscrollbarのドラッグを横取りしない。終端、focus喪失、取り外しで所有を解除する。慣性移動と仮想化は対象に含めない。独自の選択やドラッグ部品はElementのPreservePointerGestureで同じ仲裁へ参加する。

BringIntoViewは対象のローカル矩形を祖先へ順に変換し、各viewportで必要な位置だけを変更する。各段階の適用済み移動と両軸で内側のクリップと交差する範囲を次の祖先へ渡す。完全に隠れる矩形は祖先の移動を要求しない。通常の子Boundsを使うため、文字編集固有の水平表示位置はそのまま維持する。focus変更では次の配置更新境界へ移動要求を予約し、通常のDispatch内では配置を実行しない。

ScrollBarは有限制約からtrackと最小thumb長を計算する。thumbの押下位置を保持してドラッグし、trackは表示長単位、keyboardは矢印、PageUp、PageDown、Home、Endで同じcontrollerを操作する。内部Buttonと追加の意味情報IDを作らない。既存のValueに上限に対する現在位置を0〜1で公開し、値の設定機能は付けない。

### 自動操作の境界

ローカルのScrollAsyncは通常のwheelを排他的入力leaseへ配送する。リモートのschemaVersion 5のscroll操作はpositionを符号付き論理差分として扱い、OverrideInput権限と通常のleaseを要求する。既存のsnapshotのフィールド数は変更せず、未知のscroll roleはnoneへ投影するため、v1〜v4の厳密な読取器を維持する。

### 契約とAPI差分

基準コードに対して、次の公開境界を追加する。生成factoryの値引数は既存のUiArgumentに従う。

```diff
+namespace Lumyte.UI
+{
+    // 借用先のviewportは一つ。操作は接続先threadで逐次行う。
+    public sealed class ScrollController : IDisposable
+    {
+        public float Offset { get; }
+        public float Extent { get; }
+        public float Viewport { get; }
+        public float Maximum { get; }
+        public bool IsAttached { get; }
+        public bool IsDisposed { get; }
+        public Action<float>? OnChanged { get; set; }
+        // finiteを要求し、現在範囲へ制限する。
+        public void ScrollTo(float offset);
+        // 別threadからは借用dispatcherへ明示的に配送する。
+        public ValueTask ScrollToAsync(IUiDispatcher dispatcher, float offset, CancellationToken cancellationToken = default);
+        public void Dispose();
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public partial class ScrollView : Widget
+            {
+                public ScrollView();
+                public WidgetChildren Children { get; }
+                public ScrollController? Controller { get; set; }
+                public UiOrientation Orientation { get; set; }
+            }
+        }
+    }
+    public sealed record UiWheelInput(Vector2 Position, Vector2 Delta) : UiInput;
+    public enum UiPointerDevice { Mouse, Touch, Pen }
+    public partial class UiElement
+    {
+        // 独自の編集・ドラッグが祖先panに優先する。既定false。
+        protected virtual bool PreservePointerGesture { get; }
+    }
+    public sealed partial class UiSurface
+    {
+        // 接続済み対象とfinite矩形を要求する。
+        public bool BringIntoView(UiElement element, Rect2D rectangle);
+    }
+}
+namespace Lumyte.UI.Controls
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public partial class ScrollBar : Lumyte.UI.Compose.Definitions.Widget
+            {
+                public ScrollBar();
+                public ScrollController? Controller { get; set; }
+                public UiOrientation Orientation { get; set; }
+                // 既定16、finiteかつ非負。track長以下へ制限する。
+                public float MinimumThumbSize { get; set; }
+                // 既定20、finiteかつ正。
+                public float LineStep { get; set; }
+            }
+        }
+    }
+}
```

## 検討した代替案

描画だけをtranslateする方式は、hit testと文字入力の座標更新を別に実装する必要があるため採用しない。内容のSizeをviewportへ縮める方式は一方向配置の確定サイズを破るため採用しない。宣言自身が位置を持つ方式は同じ宣言を複数surfaceへ接続した際に状態が混在するため、Elementまたは明示的な借用controllerへ分離する。

## 結果と影響

既存の配置と座標変換を通して、スクロール後の描画、入力、意味情報、文字入力候補を一致させられる。利用側は有限のviewportとcontrollerの借用寿命を管理する必要がある。

検証では有限性、0寸法、内容縮小、入れ子残余、子一回配置、capture取消、例外と再入、二重接続の旧ツリー維持を確認する。文字編集の非0位置と候補座標、通常のButton操作、ScrollBar終端、状態変更後の保持描画も同じ画面で確認する。
