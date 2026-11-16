# ADR-UI-0029: 一回の子計測で残余配分・折返し・明示セル配置を行う

- 状態: 採用
- 日付: 2026-11-16
- 基準コミット: `de53ffd947c17fddb1562fafcee228d4e7cd9d67`

## 背景

画面の作成者は残った幅を複数の領域へ配分したり、項目を折り返したり、列位置を揃えたりしたい。基準コードの `src/UI/Lumyte.UI/StackElement.cs` は主軸を無制限にして順に配置するため、これらを表現するには独自コンテナーが必要になる。

[UI-0013の配置](UI-0013-component-catalog-and-delivery-plan.md#配置とcomposition)と[レイアウト候補](UI-0013-component-catalog-and-delivery-plan.md#1-レイアウトスクロール-14種類)のC4を具体化する。[UI-0003の一回の処理](UI-0003-layout-and-invalidation.md#一回の処理とカスタムコンテナー)と[UI-0024の修飾](UI-0024-ordered-layout-and-decoration-modifiers.md#一方向layoutと寸法)は維持する。

## 決定

Flex、Expanded、Wrap、Grid、GridCellをLumyte.UIへ追加する。既存Compositionの通常引数・with・通常内容で宣言し、同じElementツリーと一回のLayoutChild呼出しで配置する。Stackの制約とoverflowの意味は変えない。

### 利用者の変更前・変更後

変更前は横Stackへ固定幅を指定できるが、残余幅を重みで配る機能はない。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var before = Stack(orientation: UiOrientation.Horizontal)[
    SizedBox(width: 80), SizedBox(width: 100)];
```

変更後は、先頭80を保ち、残りを1対2へ配分できる。親幅400、gap10なら残り300を100と200へ配る。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var after = Flex(orientation: UiOrientation.Horizontal, spacing: 10)[
    SizedBox(width: 80),
    Expanded()[SizedBox(with: [Align.Center()], width: 20, height: 20)],
    Expanded(weight: 2)[SizedBox()]];
var tags = Wrap(spacing: 4, runSpacing: 6)[
    SizedBox(width: 60, height: 20), SizedBox(width: 60, height: 30)];
var table = Grid(columns: new UiGridTrack[] { new(80), new(1, true) })[
    GridCell()[SizedBox()], GridCell(column: 1)[SizedBox()]];
```

### 共通契約と検証の時点

宣言は値と通常内容だけを保持する。同じ宣言を複数Surfaceで使えて、幾何・cache・購読はElementごとに独立する。layout中に宣言や所有ツリーを変更しない。各直接子へLayoutChildとSetChildOffsetを各一回使い、子の確定Sizeを変更しない。非表示子は既存基盤で0となり計測overrideを呼ばない。Flexの重み・間隔・配置とWrapの項目判定はElementのLayoutVisibleを使い、presenceがHiddenなら除外する。終了transition中はまだ配置対象なので、宣言のIsPresentだけで判定しない。

gapと固定長は有限非負、重みは有限正、行列番号は非負、spanは正とする。不正値はsetterでArgumentOutOfRangeException、null track列はArgumentNullExceptionとし、失敗した代入は保持値を変えない。track列はsetterでコピーし、外部listの変更を採用しない。

ExpandedとGridCellはそれぞれ対応する親の直接の通常内容に限り、0〜1子を持つ。親の型・配置範囲・重複を公開前の投影ツリーで検証する。保持要素の移動、slot更新、Signalによるtrack数変更にも適用する。失敗時は旧宣言・所有権・focus・購読を保持し、prepareやcallbackでlayoutしない。採用後の制約依存の失敗にはUI-0003の中断規則を使い、途中の幾何の巻戻しを保証しない。

### FlexとExpanded

向きはFlexがVertical、WrapがHorizontalを既定とし、未定義enumを拒否する。交差軸はstart配置で、兄弟へ合わせる再計測はしない。Flexは全可視非Expanded子へ主軸[0,+Infinity]、交差軸[0,親max]を先に渡す。可視子間だけのSpacingと非Expandedサイズの合計を親主軸maxから引き、0以上の残余をExpanded.Weightの比で割り当てる。Expandedへ主軸tight、交差軸looseを渡し、最終配置順は宣言順とする。

Expandedがあれば、非表示でも主軸の有限maxを要求する。Expandedなしの無制限軸はStackと同じ内容サイズになる。非Expandedが高さ80を二つ返す高さ100の縦Flexでは両者を80のまま保ち、後続Expandedは0、位置160となる。gapは縮めない。空Flexはbounded軸でmax、unbounded軸で親minになる。

Expanded自身のWidth/Heightも親tight制約を優先する。内容を小さく置くには子のwithのAlignを使用する。親の割当を子の計測後にサイズ変更で実現しない。

### Wrap

各子へ親の両軸maxを保ってminを0にした制約を一度だけ渡す。返ったサイズとSpacingを現在のrunへ加え、有限主軸maxを超える場合だけ次のrunへ送る。等しい場合は折り返さない。run内の交差軸位置はstart、次のrunは前runの最大交差サイズとRunSpacingを足した位置となる。

幅100、子60×20と60×30、Spacing4、RunSpacing6なら位置は(0,0)と(0,26)である。高さ100の縦向きなら軸を交換する。無制限主軸では折り返さず、交差軸は有限でも内容runの総量がoverflowしてよい。子は残り幅ではなく親maxで計測するので、折返しによる再計測はない。非表示子にgapを置かず、0寸法の可視子は一つの項目として扱う。

### GridとGridCell

RowsとColumnsはそれぞれ既定で一つの比率1のtrackを持つ。空配列を許し、その軸にセルを置けない。子は明示的GridCellだけに限定し、自動配置を導入しない。非表示セルも範囲と重複検査へ含める。spanが占有する長方形同士の重なりを拒否し、隣接は許す。

軸ごとに固定長と全gapを先に足し、有限maxから引いた非負の残余を比率trackへ配る。比率が一つでもあれば対応軸maxは有限でなければならない。固定trackだけなら無制限maxを許す。固定長とgapは親より大きくても縮めず、比率部分だけ0になる。Grid自身のbounded軸は親max、unbounded軸はtrack合計を親minへclampする。

幅300、列80・比率1・比率3、gap10なら残余200、列幅80・50・150、原点0・90・150となる。後二列をspanするセル幅は210である。幅50なら列80・0・0で原点0・90・100となり、親幅は50のままclipの有無で表示と入力だけを制限する。0固定trackの単独セルへは0tightを渡す。セルにはspan内のtrack合計と内部gapをtightで渡し、内容もその制約を継承する。小さい内容はwithのAlignで表現する。

### 数値と幾何

合計と重み配分はdoubleで計算してからfloatへ変換する。float.MaxValueの重み二つでも重み合計をfloatでoverflowさせない。各サイズは独立に最寄りfloatへ丸め、末尾セルへ誤差を集約しない。例えば幅1の三等分は各約0.33333334となり、微小な端の誤差を許す。無制限の内容サイズや子位置・spanサイズを有限floatで表せなければInvalidOperationExceptionとし、NaNや無限を公開しない。

入力、capture、focus、clip、描画、Automationは既存の本体offset変換を共用する。layout部品へ新しい操作capabilityやcommandは追加しない。修飾の余白・整列と組み合わせても本体原点の加算は一度である。

### 公開APIの差分

基準コミットの実装に次を追加する。引数と内容の生成は基準コードの `src/Core/Lumyte.Composition.Generators/CompositionGenerator.cs` を使い、追加のフレームワークを要求しない。

```diff
 namespace Lumyte.UI
 {
+    // defaultは0固定。値検証は共通契約を参照。
+    public readonly record struct UiGridTrack
+    {
+        public UiGridTrack(float value, bool isRatio = false);
+        public float Value { get; }
+        public bool IsRatio { get; }
+    }
     public sealed class UiWidgetValidationContext
     {
+        // 公開前の通常slotの内容。保持された宣言の古いChildrenを参照しない。
+        public IReadOnlyList<Compose.Definitions.Widget> GetSlotChildren(WidgetChildren slot);
     }
     public static partial class Compose
     {
         public static partial class Definitions
         {
+            public partial class Flex : Panel
+            {
+                public Flex();
+                [ComposeParameter] public UiOrientation Orientation { get; set; }
+                [ComposeParameter] public float Spacing { get; set; }
+            }
+            public partial class Expanded : SizedBox
+            {
+                public Expanded();
+                // 既定1。直接のFlex通常内容に限定。
+                [ComposeParameter] public float Weight { get; set; }
+            }
+            public partial class Wrap : Panel
+            {
+                public Wrap();
+                [ComposeParameter] public UiOrientation Orientation { get; set; }
+                [ComposeParameter] public float Spacing { get; set; }
+                [ComposeParameter] public float RunSpacing { get; set; }
+            }
+            public partial class Grid : Panel
+            {
+                public Grid();
+                [ComposeParameter] public IReadOnlyList<UiGridTrack> Rows { get; set; }
+                [ComposeParameter] public IReadOnlyList<UiGridTrack> Columns { get; set; }
+                [ComposeParameter] public float RowSpacing { get; set; }
+                [ComposeParameter] public float ColumnSpacing { get; set; }
+            }
+            public partial class GridCell : SizedBox
+            {
+                public GridCell();
+                // 既定0。spanは既定1。直接のGrid通常内容に限定。
+                [ComposeParameter] public int Row { get; set; }
+                [ComposeParameter] public int Column { get; set; }
+                [ComposeParameter] public int RowSpan { get; set; }
+                [ComposeParameter] public int ColumnSpan { get; set; }
+            }
         }
     }
 }
```

## 検討した代替案

- Stackへ残余配分を追加する: 記述は少ないが、既存の無制限主軸とoverflowを変えるため独立Flexを選ぶ。
- 子を計測してから残り幅で再計測する: 内容に合う配置が可能だが、一回計測と幅依存の高さの確定規則に反する。
- 自動配置やAuto列を持つGrid: 短い宣言で済むが、挿入時の位置と計測順が暗黙になるため明示セルと固定・比率だけにする。
- 外部UIのlayoutエンジンを使用する: 既存Element、修飾、制約と同じ所有権・一回計測を維持する変換が必要になる。小さな内在的配置処理なので依存を追加しない。

## 結果と影響

利用者はCompositionだけで残余配分・折返し・明示セル配置を使える。フォント、テーマ、Platform、Controlsの暗黙依存をcoreへ追加せず、既存StackとC1の座標契約を維持する。

利用者は有限軸の指定とセル範囲を管理する。intrinsic、Auto、baseline、兄弟の最大寸法へ揃える再計測は対象外である。受入れ検証では上記数値例、tight・loose・unbounded・空・0・overflow、各子一回、投影ツリーの拒否と旧状態保持、Signal更新、複数Surface、通常入力とAutomationの本体offset・clip・focusを確認する。CPUとHeadlessの検証はGPU描画やOSアクセシビリティの実機保証を意味しない。
