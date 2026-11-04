# ADR-UI-COMPONENTS-0005: SizedBoxによる単一内容の寸法指定と空領域

- 状態: 提案
- 日付: 2026-11-05
- 基準コミット: `8d2b1a7f30f09cc086da5bb422740289eee7a9d5`
- 変更関係: 独立した追加
- 変更元: [UI-0013のレイアウト・スクロール](../ui/UI-0013-component-catalog-and-delivery-plan.md#1-レイアウトスクロール-14種類)の未実装SizedBox候補を具体化する

## 背景

画面作成者は、一定の領域を空ける場合や、単一内容へ寸法制約を渡す場合に、見た目や操作を持たない箱を使いたい。基準コードにはWidth/HeightとPanelはあるが、Panelは複数の子を重ねる部品であり、0〜1個の内容に限定した空間確保の責務を表していない。

根拠は[UI-0002の子プロパティと所有権](../ui/UI-0002-widget-tree-and-composition.md#コンポーネントごとの子プロパティ)、[UI-0003の制約と確定サイズ](../ui/UI-0003-layout-and-invalidation.md#制約と確定サイズ)と基準コード `src/UI/Lumyte.UI/PanelElement.cs` のLayoutOverride、`src/UI/Lumyte.UI/UiBoxConstraints.cs` とする。カテゴリui-componentsは個別部品の利用方法・制約・入力の責務を扱い、uiカテゴリの共通ツリー・実行基盤と区別する。

## 決定

Lumyte.UIへSizedBoxを追加し、0〜1個の子へ一度だけ制約を渡す寸法指定用Widgetとする。既存のWidth/Heightと共通修飾処理を使用し、空でも有限サイズを返すが、描画や操作の状態は新設しない。

### 利用者の変更前・変更後

変更前はPanelで空領域を表せるが、複数childも受け付け、未指定のbounded軸を埋める。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var space = Panel(width: 24, height: 8);
```

変更後は同じ空領域をSizedBoxと明示できる。Panelの挙動は変えない。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var space = SizedBox(width: 24, height: 8);
var content = SizedBox(width: 24)[
    Rectangle(width: 10, height: 8)
];
```

上記は追加APIの設計であり実装済みではない。親が十分なloose制約を渡す場合、spaceは24×8、contentは24×8となる。Rectangleは幅24のtight制約を受けるため、要求Width=10より親由来の幅24が優先する。描画を示す例ではないため色の指定は省略する。

### 内容と一方向layout

Widgetは継承したWidth/HeightとChildren宣言slotを持ち、Elementは幾何とcacheを持つ。Childrenは0〜1個で一回だけ初期化でき、2個以上、null、循環等の不正な所有は既存の宣言検証で拒否する。型/key/親/slotによるElement照合と準備失敗時の旧tree保持を変更しない。

1. 親からの制約を共通の修飾処理へ渡す。修飾はwithの先頭を外側とし、SizedBox自身は修飾を追加しない。
2. 最内側でWidth/Heightを制約内へclampし、指定軸をtightにした本体制約をLayoutOverrideへ渡す。
3. layout対象の子があれば、その本体制約を緩めずに一度だけ子へ渡す。子が返す外寸を本体寸法とし、子の外側原点を本体原点へ配置する。
4. 子がない、または非表示等でlayout対象外なら、本体制約のminWidth/minHeightを返す。無限maxでも有限minを使い、Infinityを寸法にしない。
5. 本体の結果を修飾の逆順に戻して外寸を確定する。確定した子寸法を後からresizeしない。

未指定軸は、子があれば子の返す寸法へ追従し、空ならminを使う。Width/Heightの負値、NaN、Infinityは既存の寸法検証で拒否する。計算overflowも無効な幾何として失敗し、layout失敗で全幾何を巻き戻す保証は追加しない。

### 描画・入力と更新

SizedBoxは背景や枠を自動描画せず、子を通常の共通経路で記録する。既定のroleはNone、Focusableはfalse、Click/Edit capabilityはなく、空領域をButtonとして扱わない。IsEnabled、IsVisible、HitTestVisible、ClipToBoundsの既存共通契約は維持する。

withによる余白・枠等がある場合、親から見たSize/Boundsは外側領域、本体の自己描画と子位置は本体原点を使う。子のhit test、focus、Automation、IME、Popup位置にも同じ本体変換を適用する。SizedBoxが子の操作を代理実行したり、新しいcallbackを発火したりしない。

Width/Height、子の変更、修飾の寸法差分は共通経路でlayout・入力幾何・描画を無効化する。Widgetを直接書き換えて表示更新せず、宣言の再生成で更新する。Signalの寸法引数は既存UiArgumentを使い、独自購読やtimerを持たない。同じ宣言を複数Surfaceへ渡してもElementのcacheを共有しない。

### 公開APIの差分

比較元の実装にはSizedBoxがない。UI-0013の未実装候補名を維持し、次の宣言とchild契約を追加する。

```diff
 namespace Lumyte.UI
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
+            [Composable]
+            public partial class SizedBox : Widget
+            {
+                public SizedBox();
+                // 0〜1個、一回初期化。個数と所有権は既存slot規則で検証する。
+                [ComposeContent] public WidgetChildren Children { get; }
+                // Width/Height、共通with、値引数adapterは既存Widgetから継承する。
+                protected override UiElement CreateElement();
+            }
         }
     }
 }
```

通常の生成factoryと子インデクサを使い、独自の構築builder、返却Widgetの差し替え、メソッドチェーンは追加しない。

## 検討した代替案

- Panelで代用する: 新APIは不要だが、複数の子やbounded軸を埋める意味まで伴う。単一内容と空領域の意図を専用Widgetで表す。
- 子をlooseで測定して後から指定寸法へresizeする: 幅に依存する高さが不整合になるため、一方向layoutを維持する。
- Spacerを別Widgetとして追加する: 同じ有限寸法の空間はSizedBoxで表せるため重複させない。

## 結果と影響

空領域と単一内容の寸法指定を、見た目や入力状態を追加せず記述できる。子を持つSizedBoxは一つのElementを追加するため、単に部品自身のWidth/Heightだけで足りる場合はその指定を使える。

### 受け入れ条件

- 別assemblyからSizedBoxのfactory・子インデクサ・寸法のSignal引数を利用できる。
- 空、単一child、複数childの拒否、一回初期化、準備失敗時の旧tree保持を確認する。
- loose(100,100)で空の幅24・高さ8は24×8、幅24のみの空は24×0、childの高さ8があれば24×8となる。
- tight(10,6)では要求24×8より親が優先され、子へ10×6を一度渡す。未指定でunbounded軸なら有限の子寸法またはminへ追従する。
- 幅依存高さの子を一度だけlayoutし、サイズを後から変えない。非表示の子は既存規則どおり0となり、SizedBox自身は空の場合と同じ本体minを返す。
- 修飾なしでの配置、修飾付きの本体座標、ClipToBounds、非一様DPI、通常入力とAutomationを照合する。
- 同じkeyの再構築でElementを保持し、除去・再追加では新しい寿命となる。独自入力状態やcallbackを増やさない。

実装や受け入れ条件の実行完了を本ADRの状態から推測しない。
