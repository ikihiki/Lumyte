# ADR-UI-0024: withによる順序付き余白・枠・整列と制約

- 状態: 採用
- 日付: 2026-11-05
- 基準コミット: `8d2b1a7f30f09cc086da5bb422740289eee7a9d5`
- 変更関係: 一部変更
- 変更元: [UI-0013](UI-0013-component-catalog-and-delivery-plan.md)のC1部品区分、[UI-0003](UI-0003-layout-and-invalidation.md)の寸法と子座標、[UI-0004](UI-0004-drawing-and-clipping.md)の自己描画座標
- 変更対象: Padding、Border、Align、ConstrainedBoxを独立Widgetではなく、既存withから登録する修飾へ変更する
- 継続する判断: SizedBox、Separator、他の部品の責務と依存方向、WidgetとElementの分離、一方向layout、通常入力とAutomationの共通経路

## 背景

画面の作成者は、余白・枠・整列のたびに単一childのコンテナを重ねると、内容よりも入れ子の記述が多くなる。基準時点のUI-0013ではこれらは未実装のWidget候補であり、既存Compositionのwithは生成した同じ宣言へ設定操作を順番に適用できる。

根拠は[COMPOSITION-0001の構築・with・子要素の順序](../composition/COMPOSITION-0001-declarative-composition.md#構築with子要素の順序)、[UI-0002](UI-0002-widget-tree-and-composition.md)、[UI-0003](UI-0003-layout-and-invalidation.md)、[UI-0004](UI-0004-drawing-and-clipping.md)とする。基準コードの `src/Core/Lumyte.Composition.Generators/CompositionGenerator.cs` は `Action<T>` を同じinstanceへ適用し、`src/UI/Lumyte.UI/UiElement.Layout.cs` は子のlayoutと位置設定を分けている。以下は設計の追加であり、基準時点で使えるAPIではない。

## 決定

C1の余白・枠・整列・追加制約を、Widgetに保存する順序付きの修飾設定として提供する。利用者は既存の `with: [...]` から登録し、Elementが一方向layoutと描画・入力座標へ反映することで、返却型や子slotを変えずにネストを減らす。

### 利用者の変更前・変更後

同じButtonの周りへ12の余白を付ける例を示す。変更前の実装済みAPIではWidthとHeightは指定できるが、Buttonの外側の余白を宣言する標準機能はなく、独自Elementを実装する必要がある。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

// 変更前: この記述だけでは外側に余白は付かない。
var save = Button(width: 120, height: 32, onClick: Save)[
    Rectangle(width: 16, height: 16)
];
```

変更後の設計では、内容とcallbackを変えずにwithへ追加する。Rectangleの色とSaveの実装は省略している。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var save = Button(width: 120, height: 32, onClick: Save, with: [
    Padding.All(12)
])[
    Rectangle(width: 16, height: 16)
];
```

親がlooseで十分な領域を与える場合、外寸は144×56、Button本体は120×32になる。親がそれより小さいtight制約を与えれば親制約を優先し、本体へ渡す制約を縮める。既存の `[]` と名前付きslotは元のButtonへ接続され、factoryは引き続きButtonを返す。

修飾を組み合わせる例は次のとおり。先頭が最も外側である。

```csharp
// 上のusingに加えて using Lumyte.Graphics.Drawing2D; を使用する。
Button(with: [
    Align.Center(),
    Padding.All(12),
    Border.Stroke(new Color2D(1, 1, 1, 1), 1)
])[Rectangle(width: 16, height: 16)];
```

この例は外側から整列領域、余白、枠、Button本体の順になる。メソッドチェーンやWidgetを返す変換関数は導入しない。

### C1の範囲とカタログとの差分

UI-0013のC1で提案したSizedBoxとSeparatorは独立Widgetとして残す。SizedBoxは既存のWidth/Heightと0〜1 childを使い、空なら制約内の最小有限寸法を返す。子があれば本体制約をそのまま一度渡し、返った寸法を変更せず原点へ配置して自身の本体寸法とする。ただしlayout除外された子は既存規則で0寸法になるため、SizedBox自身は本体制約のminへclampする（子の寸法は変えない）。Separatorは向き・線の厚さ・色を持つ装飾線とし、既定ではfocus、click、独自のAutomation操作を持たない。

Padding、Border、Align、ConstrainedBoxはWidget継承と独自child slotを持たない操作定義へ変更し、単独でツリーへ置かない。これらのfactory識別子はComposeActionの生成入口として使う。生成上の設定用インスタンスを作れても、それはWidgetではなく表示対象にならない。

したがってUI-0013の46 Widget候補のうち4種類を修飾へ再分類し、表示・操作Widget候補は42種類、新規Widget候補は36種類となる。P0のWidget候補は21から17へ減り、P1=22、P2=3と9種類のrecipe候補は維持する。これは機能削減ではなく表現と集計の変更である。旧カタログ本文は履歴として保持し、この範囲の比較は本ADRから行う。

### withへの登録と所有権

ComposeActionは設定値を捕捉し、with適用時に対象Widgetの内部修飾列へ不変の値設定を追加する。外部の可変list、利用者の任意実行delegate、Element参照を修飾設定として保持しない。初期対象は本ADRの組み込み4種類に限定し、任意の修飾プラグインAPIは公開しない。

同種の修飾も上書きせず追加する。Paddingを2回指定すれば2層となる。一般の設定操作は従来どおり同じ対象へ記述順に作用し、修飾列に追加された項目だけを外側から内側へ解釈する。with自体のAction適用順序、例外伝播、factoryの戻り値は変更しない。

値の検証は操作の適用時に行い、失敗した項目は追加しない。以前のActionの副作用を巻き戻さないというComposition契約は維持する。mountedな宣言を書き換えて表示を更新せず、新しい宣言の採用を通して変更する。修飾を追加してもkey、型、親、slotが同じならElementと入力状態を保持する。

宣言の再生成では修飾列を一から構築する。保持対象の旧列へwithを再適用して項目を蓄積させない。共有した宣言から別Surfaceを作っても、寸法・各層の矩形・cacheはそれぞれのElementに置く。修飾層はWidgetやElementを追加せず、独立したkey、子、購読、mount hookを持たない。

修飾操作の引数は初期範囲では構築値であり、Signal/Computed/UiAnimatedを直接受ける新しいadapterを作らない。動的な値は構築関数内でSignalを読み、新しい修飾付き宣言を返す。UI-0019の既存再構築経路を使い、独自schedulerやtimerは作らない。

### 一方向layoutと寸法

親から来た制約を、修飾列の先頭から末尾へ渡す。最内側で既存Width/Heightを親制約内へclampしてtightにし、部品のLayoutOverrideを一度呼ぶ。結果の有限サイズを逆順に戻し、各層の位置と外寸を決める。子の確定サイズを後から変更せず、実際の各子Elementも既存規則どおり一回だけlayoutする。

- Paddingは四辺を差し引く。各軸のmin/maxは0未満にならないようclampし、無限maxは無限のままにする。戻りは本体寸法と余白の和を親制約へclampする。余白が領域を超える場合は先頭側の余白量を位置として保ち、内容は0まで縮めるが、余白値自体は比例縮小しない。clipしなければ外へ出る場合がある。
- Borderは内側へ線を描き、線幅を四辺のinsetとしてPaddingと同じ制約計算に使う。外寸より線幅が大きいときも描画を外へ広げず、利用可能な枠領域へ収める。角丸、背景fill、影は今回の範囲に含めない。
- ConstrainedBoxは追加のmin/maxを各軸の親min/maxへclampしてから内側へ渡す。親制約を優先し、矛盾した空の区間を作らない。追加制約自身のmin>maxは登録時に拒否する。
- Alignはminを0にしたloose制約を内側へ渡す。bounded軸の外寸は親max、unbounded軸の外寸は内側寸法を親制約へclampした値とする。残余寸法に0〜1の配置係数を乗じ、位置だけを変更する。Centerは両軸0.5である。

有限値同士の和や差がoverflowした場合も不正な幾何として失敗させ、InfinityやNaNを公開しない。修飾がないときは基準コードと同じ寸法・配置になる。IsVisible=false等の既存layout除外は修飾全体へ適用する。

Element.SizeとBoundsは親が扱う外側の寸法・位置のままとする。新しいContentBoundsはElementの外側原点を基準とする最内側の本体矩形、ContentSizeはその寸法とする。部品のLayoutOverrideは本体制約を受け取り、RecordOverride/OnRecord、子の位置指定とOnInputのlocal座標は本体原点を使う。組み込み部品の自己描画・inside判定はContentSizeを参照するよう変更する。

これにより既存の独自Elementでも修飾なしの意味は維持されるが、修飾に対応させるには本体の自己描画・判定でSizeの代わりにContentSizeを使う必要がある。親は子のSizeを使い続ける。共通基盤は子offsetを本体座標として保持し、親本体からSurfaceへ変換する際だけ各祖先のContentBoundsの位置を加える。

### 描画・入力・Automationの幾何

共通基盤はlayout成功時に各修飾層の矩形と本体原点を確定し、描画・hit test・focus表示・Automation・IME・Popup anchorへ同じ変換を使う。DPI変換はUI-0007のhost境界で一度だけ行う。公開した幾何をGPU処理が後からElement経由で読むことは許可せず、既存の描画snapshotと資源寿命に従う。

枠は該当層の矩形へ描き、本体と子の記録とはcanvas状態を分離する。修飾は暗黙のclipを追加しない。ClipToBounds=trueならそのElementの外側Boundsで描画と入力をclipし、子固有のclipも維持する。

Padding、Border、Alignの本体外領域は元のButton等のクリック領域に含めず、その領域で新しい入力targetを作らない。OnInputとOnProbePointerDownには同じ本体ローカル座標を渡し、capture済みpointerは本体外でも従来どおり配送する。修飾により本体位置が変わっても、型とkeyが維持されれば押下やfocusを理由なく取り消さない。

既存Automation nodeのBoundsは外側配置を示し、修飾層のnodeは追加しない。snapshotのContentBoundsはSurface座標の本体矩形、ActionableBoundsは本体と実効入力clipの交差を値として保持する（Element.ContentBoundsは外側原点からの相対矩形のまま）。Clickは本体の操作可能領域と既存probeの一致から点を選び、余白中心のクリックで成功扱いにしない。本体が0面積、clip外、他要素に遮蔽されていれば通常の操作不能結果とする。IME caret、選択範囲、Popup anchorは本体座標からSurfaceへの共通変換を使い、余白offsetを二重適用しない。

### 更新・失敗と無効化

ElementのPrepareUpdateで次の修飾列を検証し、公開前の失敗では旧宣言・入力状態・購読・修飾cacheを保つ。採用後は新しい列へ交換し、旧列の幾何や比較cacheを参照しない。layout失敗時はUI-0003と基準コードの既存停止規則に従い、全geometryのrollbackは新しく保証しない。

修飾の追加・削除・並べ替え、余白・線幅・制約・整列係数の差分はlayout、入力幾何、描画を無効化する。枠色だけの差分は描画を無効化し、layoutを再実行しない。共通CompareConfiguration経路で判定し、色変更から不要なhostサービス検証やIME文書変更を発生させない。

共有要素のcapture再利用では、修飾列の差分に加えて、修飾がある要素の本体矩形と各中間層の矩形の一致を確認する。枠やinsetを含む内容を単純に伸縮すると線幅や余白が変わるためである。修飾がない共有rootの位置・寸法差は、既存のcomponent固有の内容比較と子の幾何比較を通れば再利用を妨げない。保持した描画を演出中の外側矩形へ伸縮する既存契約を維持する。

### 公開APIの差分

以下は基準コードへの追加を示す主要宣言であり、実装stubではない。Color2Dは既存のLumyte.Graphics.Drawing2D、Vector2はSystem.Numericsを使用する。privateなComposeActionの定義と、生成される利用者向けのAction入口を区別する。

```diff
 namespace Lumyte.UI
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
+            // Widgetではない操作定義。生成factoryは操作の入口を識別する。
+            [Composable]
+            public partial class Padding
+            {
+                public Padding();
+                // finite・非負。適用時に不正値はArgumentOutOfRangeException。
+                [ComposeAction] private static void All(Widget target, float value);
+                [ComposeAction] private static void Sides(Widget target, float left, float top, float right, float bottom);
+            }
+            [Composable]
+            public partial class Border
+            {
+                public Border();
+                // widthはfinite・非負。0はinsetも描画もなし。色は既存Color2D規則。
+                [ComposeAction] private static void Stroke(Widget target, Color2D color, float width);
+            }
+            [Composable]
+            public partial class Align
+            {
+                public Align();
+                [ComposeAction] private static void Center(Widget target);
+                // x/yはfiniteな0〜1。不正値は適用時に拒否する。
+                [ComposeAction] private static void At(Widget target, float x, float y);
+            }
+            [Composable]
+            public partial class ConstrainedBox
+            {
+                public ConstrainedBox();
+                // UiBoxConstraintsの検証済み値を借用せず値コピーして追加する。
+                [ComposeAction] private static void Limits(Widget target, UiBoxConstraints constraints);
+            }
+            [Composable]
+            public partial class SizedBox : Widget
+            {
+                public SizedBox();
+                // 0〜1 child、一回初期化。Width/Heightは継承した本体寸法。
+                [ComposeContent] public WidgetChildren Children { get; }
+            }
+            [Composable]
+            public partial class Separator : Widget
+            {
+                public Separator();
+                // 既定Horizontal。未定義enum値は宣言検証で拒否。
+                [ComposeParameter] public UiOrientation Orientation { get; set; }
+                // 既定1。finite・非負。
+                [ComposeParameter] public float Thickness { get; set; }
+                // 既定は透明。色を明示して使う。
+                [ComposeParameter] public Color2D Color { get; set; }
+            }
         }
     }
     public partial class UiElement
     {
-        // 親ローカル座標の矩形。
+        // 親本体ローカル座標での外側矩形。寸法はSizeと同じ。
         public Rect2D Bounds { get; }
-        // 部品の確定サイズ。
+        // 修飾を含めた外寸。修飾なしでは従来と同じ。
         public Vector2 Size { get; }
+        // 外側原点から見た本体矩形。修飾なしでは原点0、寸法はSizeと同じ。
+        public Rect2D ContentBounds { get; }
+        public Vector2 ContentSize { get; }
-        // 寸法指定を適用した制約で、有限サイズを計算する。
+        // 修飾列を通した後にWidth/Heightを適用した本体制約で、本体サイズを返す。
         protected virtual Vector2 LayoutOverride(UiBoxConstraints constraints);
-        // 直接の子を配置し、子の寸法は変更しない。
+        // 親本体原点から子の外側原点へのoffset。子の寸法は変更しない。
         protected void SetChildOffset(UiElement child, Vector2 offset);
-        // 要素のローカル原点とclipを適用済みのcanvasで記録する。
+        // 本体原点と既存clipを適用済みのcanvasで記録する。自己描画にはContentSizeを使う。
         protected virtual void RecordOverride(DrawingCanvas2D canvas);
-        // 要素のローカル描画を行う。
+        // 本体ローカル描画を行う。canvas状態の保存・復元規則は維持する。
         protected virtual void OnRecord(DrawingCanvas2D canvas);
-        // 同期contextで入力を処理する。
+        // 本体ローカルのPointerPositionと本体のIsInsideで処理する。Input自体のSurface座標は維持する。
         protected virtual UiInputResult OnInput(in UiInputContext context);
-        // ローカル位置でDown配送を予測する。
+        // 本体ローカル位置で、通常OnInputと同じDown配送を予測する。
         protected virtual UiInputResult OnProbePointerDown(Vector2 localPosition);
     }
     public sealed class UiAutomationNode
     {
+        // Surface論理座標の本体矩形。Boundsは外側矩形のまま。
+        public Rect2D ContentBounds { get; }
+        // 本体と実効入力clipの交差。通常probeで操作対象の一致を別途確認する。
+        public Rect2D ActionableBounds { get; }
     }
     public readonly struct UiInputContext
     {
-        // 受信要素ローカルのpointer座標。非pointerではnull。
+        // 受信要素の本体原点を基準とするpointer座標。非pointerではnull。
         public Vector2? PointerPosition { get; }
-        // 受信要素の入力領域内かを示す。
+        // clipを考慮した本体の入力領域内かを示す。修飾による余白・枠を含めない。
         public bool IsInside { get; }
     }
 }
```

生成入口の例は `Action<Widget> All(this PaddingFactory factory, float value)` となり、既存の反変性でButton等のwithへ渡せる。操作定義型はUI本体内に置き、CoreのCompositionへGraphics/UI依存、返却型変換、slot転送機構を追加しない。

SeparatorはHorizontalなら高さをThicknessの要求値、幅をbounded軸のmaxまたはunbounded軸のminとして、親制約へclampする。Verticalは軸を交換する。線は本体の中央に置き、実際の本体矩形内へ収める。明示Width/Heightと親tight制約は他のWidgetと同じ優先規則を使う。

## 検討した代替案

- 独立コンテナだけで組み立てる: 順序と所有権が明白だが、余白と枠のために記述とElementの階層が増える。今回は表示ツリーを増やさない修飾列へ置き換える。
- 全WidgetへPadding/Border/Alignmentの単一プロパティを追加する: 短く書けるが、同じ修飾の繰り返しや枠の内外を順序で指定できない。
- withの操作で返却Widgetを差し替える: 既存コンテナを再利用できるが、具体的な戻り値型と後続の子slotの接続先を変える必要がある。同じ宣言への設定操作を維持する方が互換性を保てる。
- Widgetへのメソッドチェーン: 入れ子を避けられるが、既存の引数・with・子インデクサによる構築規則と異なる入口を増やすため選ばない。

## 結果と影響

利用者は値とcallbackを通常引数、修飾をwith、内容を子インデクサへ分けて記述できる。修飾自身のElement、入力target、資源寿命は増えないが、各層の計算と保存は必要であり、性能改善は測定なしに保証しない。

既存Compositionのfactory型とAction契約は維持する。一方、UI基盤のlayout・描画・入力・Automation・IME・Popup座標変換と、Sizeを本体寸法として使う独自Elementには対応が必要になる。Recipeの子slot転送、Grid/Flex、任意のユーザー定義修飾、OSアクセシビリティbridgeは今回決めない。

### 受け入れ条件

- Generatorを介し、Lumyte.UI外のnamespaceを持つ別assemblyから明示のusingでAll/Sides/Stroke/Center/At/Limitsをwithへ渡せる。戻り値型、通常の子、Button.Background等の名前付きslotが変わらない。
- 修飾の順序と繰り返しを検証する。Padding→BorderとBorder→Paddingで枠位置が異なり、修飾なしでは既存のlayoutと画素が一致する。
- tight/loose/unbounded、空内容、幅依存の高さ、親より大きい余白、0寸法、数値overflow、NaN/Infinity、不正制約を検証する。各子のlayout回数は一回を維持する。
- SizedBoxの0〜1 child、一回初期化、空と非空の有限寸法、片軸指定、unbounded軸での子寸法への追従、Separatorの両向きと無操作性を検証する。
- 再構築、Signal更新、同一宣言の複数Surface、並べ替えで修飾の重複追加・cache共有がない。型/key/親/slotを維持した追加・削除でElement、focus、Automation IDを維持する。
- 本体・余白・枠・clip・captureの境界でpointer、keyboard、Automationの操作対象とcallback回数が一致する。余白はクリックできず、装飾だけのrole偽装でClick可能にならない。
- 入力取消、非表示、無効化、除去、PrepareUpdate失敗、layout失敗で既存の寿命と停止規則を維持する。
- 非一様DPI、Hosted表示、IME caret/候補、Popup anchor、focus表示が同じ本体変換を使う。描画canvas状態とGPU資源の既存寿命を壊さない。
- 枠色のみの更新でlayoutを再実行せず、寸法・順序変更では必要なlayout/入力/描画を更新する。独自Elementの移行例と無修飾の互換性を確認する。

本文は設計判断を示すもので、実装済みまたは上記検証が実行済みであることを意味しない。
