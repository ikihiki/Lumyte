# ADR-UI-COMPONENTS-0003: Alignを同じWidgetの整列設定として提供する

- 状態: 提案
- 日付: 2026-11-05
- 基準コミット: `8d2b1a7f30f09cc086da5bb422740289eee7a9d5`
- 変更関係: 一部変更
- 変更元: [UI-0013](../ui/UI-0013-component-catalog-and-delivery-plan.md#1-レイアウトスクロール-14種類)のAlign候補
- 変更対象: 独立Widgetと0〜1 childの候補を、非Widgetの整列操作定義へ変更する
- 継続する判断: loose制約で一度layoutして位置だけを決める責務、他部品の責務と依存方向、WidgetとElementの分離

## 背景

画面の作成者はButtonを親領域の中央に置きたいが、基準時点のWidth/Heightだけでは位置を指定できない。UI-0013のAlignは未実装のWidget候補であり、利用可能な整列コンテナではない。

根拠は基準コミットの `src/UI/Lumyte.UI/Compose.Button.cs`、`src/UI/Lumyte.UI/Compose.Rectangle.cs`、`src/UI/Lumyte.UI/UiBoxConstraints.cs`、`src/UI/Lumyte.UI/UiElement.Layout.cs` と、[UI-0003](../ui/UI-0003-layout-and-invalidation.md)の一方向layoutとする。以降のソース参照も冒頭の基準コミットに固定する。

## 決定

Alignを、既存のwithから同じWidgetへ順序付き整列設定を追加する操作定義として提供する。返却Widgetと子slotを差し替えず、同じElement内で制約と本体位置を計算し、整列だけの表示階層を増やさない。

`ui-components` は各部品の公開操作・固有動作・受け入れ条件を担当する。`ui` はツリー、layout、描画、入力など複数部品に共通する基盤を担当し、既存ADRは移動しない。

### 利用者の変更前・変更後

200×100の親領域へ120×32のButtonを中央配置する。変更前の実在APIでは次の記述だけで中央配置を要求できず、親側の独自layoutが必要となる。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var button = Button(width: 120, height: 32)[
    Rectangle(width: 16, height: 16)
];
```

変更後は今回の提案である。Rectangleの色とSurfaceへの取り付けは両例とも省略する。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var button = Button(width: 120, height: 32, with: [Align.Center()])[
    Rectangle(width: 16, height: 16)
];
```

親が200×100のtight制約を渡すと外寸は200×100、本体は120×32、本体原点は(40,34)となる。通常の子と `Button.Background(...)` の名前付きslotは同じButtonへ接続する。

### 順序と整列の契約

[COMPOSITION-0001の構築・with・子要素の順序](../composition/COMPOSITION-0001-declarative-composition.md#構築with子要素の順序)を維持する。Actionは同じ宣言へ記述順に作用し、修飾列の先頭が最も外側になる。制約は外側から内側へ、確定サイズは内側から外側へ伝える。

Alignはminを0にし、maxを保持したloose制約を内側へ渡す。最内側で既存Width/Heightを到達した制約へclampしてtightにし、本体のLayoutOverrideを一度呼ぶ。実際の子Elementも一回だけlayoutし、配置のために確定サイズを変更しない。

bounded軸の外寸は親max、unbounded軸は内側の有限寸法を親制約へclampした値とする。位置は `(外寸 - 内側寸法) * 係数` で、Centerは両軸0.5、Atは軸ごとに0〜1を指定する。Infinityを外寸に採用せず、Infinity同士の減算を行わない。

同種のAlignを複数登録すれば上書きせず入れ子にする。200×100内の `Align.At(0, 0), Align.Center()` は内側Alignが200×100を占めるため、本体は中央のままになる。

係数のNaN、Infinity、範囲外はAction適用時にArgumentOutOfRangeExceptionで拒否し、その項目を追加しない。先行Actionの副作用は巻き戻さない。有限演算のoverflowや非有限幾何はlayout失敗とし、NaNやInfinityを公開しない。

### 宣言の寿命と本体の幾何

操作は不変の値設定をWidget内部の列へ追加し、Elementや外部可変listを保持しない。再構築では列を一から作り、旧宣言への再適用で蓄積させない。動的な係数は[UI-0019](../ui/UI-0019-signals-and-reactive-state.md)の構築関数内で読み、新しい宣言へ反映する。

型・key・親・slotを維持する更新ではElement、focus、capture、Automation IDを保持する。各層の矩形とcacheはElementごとに持ち、別Surfaceへ共有しない。追加・削除・順序・係数の変更はlayout、入力幾何、描画を無効化する。

PrepareUpdateで次の列を検証し、公開前の失敗では旧宣言・入力状態・cacheを保つ。採用後は旧列の幾何を破棄する。layout失敗は既存停止規則を使い、全幾何のrollbackは新しく保証しない。非表示などのlayout除外は修飾全体へ適用する。

Element.Size/Boundsは外側の寸法・位置を示す。ContentBoundsは外側原点から見た本体矩形、ContentSizeは本体寸法とし、自己描画と入力判定はContentSizeを使う。描画・子offset・OnInput/OnProbePointerDownのlocal座標は本体原点を基準とする。

共通基盤が本体offsetを祖先ごとに一度だけ合成し、hit test、focus、Automation、IME、Popup anchorへ同じ変換を使う。整列の空き領域はButtonのクリック領域にならず、capture済みpointerの配送は維持する。Automation Boundsは外側、Click点は実際の本体操作領域から選ぶ。

Alignは暗黙のclip、Element、入力target、子slot、key、購読、mount hookを追加しない。ClipToBoundsは外側Boundsへ適用し、描画snapshotとGPU資源寿命は既存規則を維持する。独自Elementは本体の描画・判定をSizeからContentSizeへ移し、親は子のSizeを使い続ける。

### 公開APIの差分

以下は基準コードへの追加宣言である。属性は `Lumyte.Composition`、Actionは `System`、Widgetは `Compose.Definitions.Widget` を指す。生成は `src/Core/Lumyte.Composition.Generators/CompositionGenerator.cs` に従う。

```diff
 namespace Lumyte.UI
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
+            // Widgetを継承せず、表示対象でもchild所有者でもない。
+            [Composable]
+            public partial class Align
+            {
+                public Align();
+                // 両軸の係数0.5を登録する。
+                [ComposeAction] private static void Center(Widget target);
+                // finiteな0〜1。適用時に不正値はArgumentOutOfRangeException。
+                [ComposeAction] private static void At(Widget target, float x, float y);
+            }
         }
     }
+    public static class ComposeAlignCompositionExtensions
+    {
+        public static Action<Compose.Definitions.Widget> Center(this Compose.Definitions.AlignFactory factory);
+        public static Action<Compose.Definitions.Widget> At(this Compose.Definitions.AlignFactory factory, float x, float y);
+    }

 }
```

`Action<Widget>`の反変性でButton等のwithへ渡す。生成factoryは操作の識別入口で、Widget変換やメソッドチェーンは導入しない。UI-0013の未実装候補 `Align : Widget` の継承と0〜1 childを変更するのであり、実装済み型の削除ではない。

## 検討した代替案

- 独立Align Widget: 子との関係は明白だが、整列だけの階層とElementが増える。
- 単一Alignment設定: 短いが、複数登録の順序と入れ子を表現できない。
- Widgetを返す変換やチェーン: 階層を隠せるが、具体的な返却型と子slotの接続先を変えるため選ばない。

## 結果と影響

利用者は内容を変えずに整列を指定できる。共通基盤には層の幾何保存と本体座標への対応が必要で、性能向上は未測定のため保証しない。任意の修飾プラグインや直接Signalを受ける新adapterは対象外とする。

### 受け入れ条件

- tight(200×100)とloose(max=200×100)で本体120×32、Center位置(40,34)、At(1,0)位置(80,0)を確認する。
- 幅[0,+Infinity]、高さ[0,100]では外寸120×100、Center位置(0,34)。幅min=160なら外幅160、x=20となり、非有限演算しない。
- tight(80×20)ではWidth/Heightを80×20へclampし、本体offsetは(0,0)。0寸法・空内容・幅依存高さも一回layoutで有限になる。
- 重複Align、他修飾との順序、NaN/Infinity/範囲外、overflow、PrepareUpdate失敗とlayout失敗を確認する。
- 別assemblyから例のusingでCenter/Atを呼べ、戻り値型・通常の子・名前付きslot・無修飾時の寸法が変わらない。
- 再構築と複数Surfaceで蓄積やcache共有がなく、通常入力・Automation・IME・Popupとclip/captureの座標が一致する。

以上は設計の受け入れ条件であり、基準コードで実装・検証済みであることを意味しない。
