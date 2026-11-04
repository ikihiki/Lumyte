# ADR-UI-COMPONENTS-0004: ConstrainedBoxを同じWidgetへの追加制約として提供する

- 状態: 提案
- 日付: 2026-11-05
- 基準コミット: `8d2b1a7f30f09cc086da5bb422740289eee7a9d5`
- 変更関係: 一部変更
- 変更元: [UI-0013](../ui/UI-0013-component-catalog-and-delivery-plan.md#1-レイアウトスクロール-14種類)のConstrainedBox候補
- 変更対象: 独立Widgetと0〜1 childの候補を、非Widgetの追加制約操作へ変更する
- 継続する判断: 親制約を優先したmin/maxのclamp、子の一回layout、他部品の責務と依存方向

## 背景

画面の作成者はButtonの幅を範囲で制限したいが、基準時点のWidth/Heightは指定寸法であり、min/maxの範囲を宣言できない。UI-0013のConstrainedBoxは未実装のWidget候補であり、利用可能な既存APIではない。

根拠は基準コミットの `src/UI/Lumyte.UI/Compose.Button.cs`、`src/UI/Lumyte.UI/Compose.Rectangle.cs`、`src/UI/Lumyte.UI/UiBoxConstraints.cs`、`src/UI/Lumyte.UI/UiElement.Layout.cs` と、[UI-0003](../ui/UI-0003-layout-and-invalidation.md)の一方向layoutとする。以降のソース参照も冒頭の基準コミットに固定する。

## 決定

ConstrainedBoxを、既存のwithから同じWidgetへ追加制約を登録する非Widgetの操作定義として提供する。親制約内へ要求をclampして本体へ渡し、返却Widgetや子slotを置き換えずに寸法の範囲を指定できるようにする。

`ui-components` は部品固有の公開操作・動作・受け入れ条件を担当し、`ui` はツリー、layout、描画、入力など共有基盤を担当する。カテゴリ追加で既存ADRを移動せず、共通基盤と個別部品の責務を分ける。

### 利用者の変更前・変更後

幅を80〜160、高さを0〜64の範囲に収める。変更前の実在APIで指定寸法を設定する次の例は、幅120へ固定する代替であり、内容に追従する範囲制約にはならない。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var button = Button(width: 120, height: 32)[
    Rectangle(width: 16, height: 16)
];
```

変更後の提案ではWidthを外して範囲を渡す。UiBoxConstraintsの既存constructorは `(minWidth, maxWidth, minHeight, maxHeight)` であり、Rectangleの色とSurfaceへの取り付けは両例とも省略する。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var button = Button(height: 32, with: [
    ConstrainedBox.Limits(new UiBoxConstraints(80, 160, 0, 64))
])[Rectangle(width: 16, height: 16)];
```

親がloose(max=200×100)なら本体と外寸は80×32となる。親がtight(60×20)なら親を優先して60×20になる。返却型はButtonのままで、通常の子と `Button.Background(...)` の名前付きslotは元のButtonへ接続する。

### 追加制約と順序の契約

[COMPOSITION-0001の構築・with・子要素の順序](../composition/COMPOSITION-0001-declarative-composition.md#構築with子要素の順序)を維持する。Actionは同じ宣言へ記述順に作用し、修飾列の最初を最外側とする。制約は先頭から内側へ、有限の確定サイズは逆順に戻す。

各軸で親区間を[Pmin,Pmax]、追加区間を[Amin,Amax]とすると、内側へ渡す区間は `[Clamp(Amin,Pmin,Pmax), Clamp(Amax,Pmin,Pmax)]` とする。単純な区間の積集合ではなく、要求が親の外にあっても親側の境界へ寄せて空の区間を作らない。

最内側でWidth/Heightを到達した制約へclampしてtightにし、部品のLayoutOverrideを一度呼ぶ。実際の子Elementも一回だけlayoutし、返った寸法を後から変更しない。ConstrainedBox自身はその有限寸法を外寸として戻し、内側原点を移動しない。

同種のLimitsも上書きせず入れ子として追加する。親幅[0,200]で[80,160]→[20,40]なら内側は[80,80]、逆順なら[40,40]となる。両要求の同時充足ではなく、各段階で外側の制約を優先する。

UiBoxConstraintsは有限・非負のmin、min以上のmaxを持ち、maxだけに正のInfinityを許す既存の値型である。NaN、負値、min>max、無限minは既存constructorがArgumentOutOfRangeExceptionで拒否する。default値は全項目0の有効なtight制約であり、未指定の意味にはしない。

LimitsはAction適用時に値の不変条件を確認し、値コピーを列へ追加する。不正ならArgumentOutOfRangeExceptionでその項目を追加せず、先行Actionの副作用は巻き戻さない。unboundedのmaxはそのまま渡せるが、有限でない本体サイズや演算overflowはlayout失敗とし、NaN/Infinityの幾何を公開しない。

### 宣言の寿命と本体の幾何

操作は不変の設定値をWidget内部へ追加し、Elementや外部可変listを保持しない。再構築時は列を一から作り、旧列へActionを再適用して蓄積させない。動的値は[UI-0019](../ui/UI-0019-signals-and-reactive-state.md)の構築関数で読み、新しい宣言へ反映する。

型・key・親・slotを維持する更新では同じElement、focus、capture、Automation IDを保持する。各層の幾何とcacheはElementごとに持ち、別Surfaceへ共有しない。制約・個数・順序の変更はlayout、入力幾何、描画を無効化する。

PrepareUpdateで次の列を検証し、公開前の失敗では旧宣言・入力状態・cacheを保持する。採用後は旧列の幾何を破棄する。layout失敗には既存停止規則を使い、全幾何のrollbackを新しく保証しない。非表示などのlayout除外は修飾全体へ適用する。

Element.Size/Boundsは外側の寸法・位置、ContentBoundsは外側原点から見た本体矩形、ContentSizeは本体寸法とする。Limits単独では両者の寸法が等しく本体offsetは0だが、他の修飾と組み合わせても自己描画・入力判定はContentSize、描画・子offset・入力local座標は本体原点を使う。

祖先の本体offsetを一度だけ合成し、hit test、focus、Automation、IME、Popup anchorへ同じ変換を使う。余白などの本体外領域をButtonのクリック領域へ含めず、capture済みpointerの配送は維持する。Automation Boundsは外側、Click点は実際の本体操作領域から選ぶ。

暗黙のclip、Element、入力target、子slot、key、購読、mount hookは追加しない。ClipToBoundsは外側Boundsへ適用し、描画snapshotとGPU資源寿命は維持する。独自Elementの本体描画・判定はSizeからContentSizeへ移し、親は子のSizeを使い続ける。

### 公開APIの差分

基準コードへの追加は次のとおり。属性は `Lumyte.Composition`、Actionは `System`、Widgetは `Compose.Definitions.Widget` を指す。生成は `src/Core/Lumyte.Composition.Generators/CompositionGenerator.cs` に従う。

```diff
 namespace Lumyte.UI
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
+            // Widgetを継承せず、childを所有しない操作定義。
+            [Composable]
+            public partial class ConstrainedBox
+            {
+                public ConstrainedBox();
+                // 値コピーで追加。適用時に検証し、不正値はArgumentOutOfRangeException。
+                [ComposeAction] private static void Limits(Widget target, UiBoxConstraints constraints);
+            }
         }
     }
+    public static class ComposeConstrainedBoxCompositionExtensions
+    {
+        public static Action<Compose.Definitions.Widget> Limits(this Compose.Definitions.ConstrainedBoxFactory factory, UiBoxConstraints constraints);
+    }

 }
```

生成factoryは操作の識別入口であり、`Action<Widget>`の反変性でButton等のwithへ渡す。Widget差し替えやメソッドチェーンは導入しない。UI-0013の未実装候補 `ConstrainedBox : Widget` と0〜1 childを変更するのであり、基準コードに存在する型の削除ではない。

## 検討した代替案

- 独立コンテナ: 子の所有者が明白だが、追加制約だけのWidget/Elementが増える。
- Width/Heightだけを使う: 実在APIで固定寸法を指定できるが、内容に応じた範囲を表現できない。
- 単一制約プロパティや区間の積集合: 短いが順序付きの重複を失うか、親と矛盾すると空区間になるため選ばない。

## 結果と影響

利用者は同じWidgetに範囲制約を指定できるが、親が優先されるため要求範囲は絶対保証ではない。共通基盤は層ごとの処理とcacheを必要とし、性能改善は未測定のため保証しない。任意の修飾プラグインや直接Signalを受ける新adapterは対象外とする。

### 受け入れ条件

- 例の追加制約でloose(max=200×100)なら80×32、tight(60×20)なら60×20となり、実際の子も一回だけlayoutする。
- 親幅[100,200]に追加[0,50]なら[100,100]、追加[250,300]なら[200,200]を内側へ渡す。親制約との空区間を作らない。
- 親が両軸[0,+Infinity]で追加(80,160,0,64)なら例は80×32。追加maxもInfinityなら例の幅はmin=80に従い、公開サイズは有限を保つ。
- Width=120を追加[80,100]の内側で適用すると100となる。重複[80,160]→[20,40]と逆順がそれぞれ80と40になる。
- default制約では0×0。不正constructor値、非有限本体サイズ、overflow、空内容、幅依存高さ、PrepareUpdate失敗とlayout失敗を確認する。
- 別assemblyから例のusingでLimitsを呼べ、戻り値型・子slot・無修飾の寸法が変わらず、再構築と複数Surfaceで蓄積やcache共有がない。
- 他修飾との組み合わせでも通常入力・Automation・IME・Popupとclip/captureが同じ本体座標を使う。

以上は設計の受け入れ条件であり、基準コードで実装・検証済みであることを意味しない。
