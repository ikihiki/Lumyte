# ADR-UI-0003: 有限制約で計測する二段階レイアウト

- 状態: 提案
- 日付: 2026-10-27
- 対応: 実装PR 2、前提: PR 1

## 背景

[UI-0002](UI-0002-widget-tree-and-composition.md)のツリーをviewportへ配置する。描画と入力が同じBoundsを使い、子の変更後に古い配置を参照しないようにする。カスタムPanelも内部APIへ依存せず子を計測・配置できる必要がある。

## 決定

### 公開API

比較元は[UI-0001](UI-0001-composition-based-ui-system.md)に従う。Vector2はSystem.Numerics、Rect2DはGraphics.Drawing2Dを使う。先行PRのWidget等へ以下を追加する。

```diff
+namespace Lumyte.UI
+{
+    public enum UiOrientation { Vertical, Horizontal }
+    public sealed class UiSurface
+    {
+        // 既定(0,0)。各成分finiteかつ非負。不正値はArgumentOutOfRangeException。
+        public void SetViewport(Vector2 logicalSize);
+        // dirtyならルートから計測・配置。cleanなら何もしない。
+        public void UpdateLayout();
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                public Vector2 DesiredSize { get; }
+                // 親ローカル座標。更新はUpdateLayout成功時に公開。
+                public Rect2D Bounds { get; }
+                // 既定null=自動。指定値はfiniteかつ非負。
+                [ComposeParameter] public float? Width { get; set; }
+                [ComposeParameter] public float? Height { get; set; }
+                // 既定true。falseの部分木はサイズ0、overrideを呼ばない。
+                [ComposeParameter] public bool IsVisible { get; set; }
+                protected void InvalidateMeasure();
+                // availableSizeはfiniteかつ非負。基底は(0,0)を返す。
+                protected virtual Vector2 MeasureOverride(Vector2 availableSize);
+                protected virtual void ArrangeOverride(Vector2 finalSize);
+                // 現在のMeasure/Arrange phaseで直接の子にだけ使用可能。
+                protected Vector2 MeasureChild(Widget child, Vector2 availableSize);
+                protected void ArrangeChild(Widget child, Rect2D bounds);
+                // Arrange中に当該passで計測した直接の子のサイズを返す。
+                protected Vector2 GetMeasuredSize(Widget child);
+            }
+            [Composable]
+            public partial class Stack : Panel
+            {
+                public Stack();
+                // 既定Vertical。不正enumはArgumentOutOfRangeException。
+                [ComposeParameter] public UiOrientation Orientation { get; set; }
+                // 既定0。finiteかつ非負。可視の子同士の間だけに置く。
+                [ComposeParameter] public float Spacing { get; set; }
+            }
+        }
+    }
+}
```

カスタムコンテナーはPanelを継承し、Childrenを構築して上記overrideとchild操作を実装する。独自の所有リストは対象外。継承したComposeParameterも生成ファクトリの引数にする。

### 計測・配置の計算規則

全ての制約はfiniteかつ非負で、Infinityは使わない。`Measure(node, available)`は指定寸法があればその軸の制約を`min(available, explicit)`にし、MeasureOverrideを呼ぶ。返値を検証して制約内へclampし、指定軸ではexplicitを同じ制約でclampした値をDesiredSizeにする。

`Arrange(node, slot)`は指定軸を`min(slot, explicit)`、自動軸をslotサイズにし、左上へ配置する。ArrangeOverrideへ確定サイズを渡す。rootにも同じ規則を適用し、自動寸法のrootはviewport全体へstretchする。Rect2Dの位置はfinite、寸法はfiniteかつ非負を要求する。

| 要素 | Measure | Arrange |
| --- | --- | --- |
| Panel | 各可視子に同じ制約を渡し、希望値の軸別最大値 | 全子に原点0・自身の最終サイズを渡す |
| Stack | 各可視子に親の全制約を渡し、主軸の合計と交差軸の最大値を制約へclamp | 記述順に主軸の残量から希望値分を割り当てる。交差軸は全幅。gapも残量以下へclampし、末尾には置かない |
| Button | 通常内容の希望値。背景も計測するが希望値へ加算しない | 内容と背景に原点0・最終サイズを渡す |

非表示の部分木の公開Bounds・DesiredSizeは0にする。空Stackの希望値は0でSpacingを加算しない。合算はdoubleで行い制約へclampしてfloatへ戻し、overflowでInfinityを作らない。

### 無効化と失敗時の状態

寸法、可視性、Stack設定、子の置換、カスタム計測値の変更でInvalidateMeasureを呼ぶ。自身からrootまでdirtyを伝播し、同値代入では無効化しない。viewportの変更もdirtyにする。初期実装はsurface全体を再計算する。

計測・配置はpass専用の作業tableに書き、全ノードの成功後にDesiredSize/Boundsを公開してdirtyを解除する。override失敗や不正な返値では公開値を変更せずdirtyを保つ。取り外した部分木のBoundsは最後の公開値であり、未接続時の配置の有効性は保証しない。

Layout中のプロパティ・子変更とUpdateLayout再入を拒否する。child操作のnullはArgumentNullException、直接の子以外・不正数値はArgumentException、phase違反はInvalidOperationException。各可視子は一passで一度計測・配置し、重複操作や配置漏れもInvalidOperationExceptionとする。カスタムoverrideが読む公開Bounds/DesiredSizeは前passの値なので、MeasureではMeasureChildの返値、ArrangeではGetMeasuredSizeとfinalSizeを使う。

後続のDispatchとRecordはIdle中に必要なUpdateLayoutを済ませてから自身のphaseへ移る。Boundsのgetterは暗黙のレイアウトを実行しない。

## 検討した代替案

無限制約はscrollに有用だが初期の固定viewport UIへ特別な計算規則を持ち込むため採用しない。計測途中で公開Boundsを書き換える方式も、失敗後に不整合な配置が残るため採用しない。

## 結果と影響

描画とhit testへ確定した配置を渡せる。カスタムPanelも所有権検証を利用できる。一方、作業tableと全体再計算のコストがあり、初期Stackは収まらないと後ろの子の割当が小さくなる。

## 検証方針

- `LayoutTests`: 縦横Stack、空列、gap、非表示、指定寸法、自動stretch、親制約より大きい子、0viewportを確認する。
- `InvalidationTests`: 子差し替え・resize・再表示、同値代入、clean passのoverride回数を確認する。
- `CustomPanelTests`: 別assemblyのPanel派生型が継承パラメーターとchild操作を使えることを確認する。
- `LayoutFailureTests`: NaN/Infinity/負値、合算overflow、不正な子操作、例外、配置漏れで旧状態を保ち、修正後に再試行できることを確認する。
