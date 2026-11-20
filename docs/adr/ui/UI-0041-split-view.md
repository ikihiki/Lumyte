# ADR-UI-0041: 通常の子を有限領域へ分割して境界を操作する

- 状態: 採用
- 日付: 2026-11-20
- 基準コミット: `fcde70b3c84a620ab776ca19982ff678fe142315`

## 背景

画面の作成者は編集欄と一覧を同時に表示し、境界を操作して領域を配分したい。基準コードの `src/UI/Lumyte.UI/FlexElement.cs` は重みの配分を提供するが、境界の入力と受理した配分の保持を持たない。

## 決定

SplitView を Lumyte.UI へ追加する。通常の子を pane として扱い、配置の指定は [UI-0029 の親設定](UI-0029-one-pass-flex-wrap-grid.md#withの保存と更新) と同じ with 操作で保存する。境界だけを実際のフォーカス可能な要素とし、子を配置用の部品で包まない。

### 利用者の変更前・変更後

変更前は有限幅の Flex へ重みを指定できるが、境界を操作できない。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var before = Flex(orientation: UiOrientation.Horizontal)[
    SizedBox(with: [Flex.Expanded()]),
    SizedBox(with: [Flex.Expanded(weight: 2)])];
```

変更後は通常の子へ最小寸法と初期重みを指定する。境界をドラッグするかフォーカスしてキーを押すと、隣接する二領域の配分が変わる。

```csharp
var after = SplitView(orientation: UiOrientation.Horizontal)[
    SizedBox(with: [SplitView.Pane(minimum: 80)]),
    SizedBox(with: [SplitView.Pane(weight: 2, minimum: 100)])];
```

### 配置と数値

両軸の有限上限を要求し、各子を確定した tight 制約で一回だけ配置する。主軸は Orientation に従い、交差軸を埋める。空、一領域、ゼロの viewport を許す。非表示領域は配分と境界から除外するが、その実体の受理重みは保持する。

重みは有限正、最小寸法と境界寸法は有限非負とする。最小入力領域は有限正で、境界寸法以上とする。まず境界を利用可能な長さへ等比縮小し、残余へ最小寸法を割り当てる。最小寸法の合計が残余を超える場合は最小寸法を同比率で縮める。余りは受理重みで配分する。可視領域の受理重みの合計がゼロの場合は、宣言と保存重みを変えずに可視領域の余りを等分する。合算は double を使い、各区間の端を親の有限長へ制限し、負値、無限、NaN と親を超える終端を公開しない。

### 実体の受理状態

受理重みは Surface ごとの Element へ保存し、宣言や Signal へ書き戻さない。既存領域のいずれかの宣言重みが変わった場合は全領域の宣言重みへ通知なしでリセットし、無関係な引数の再構築では受理重みを維持する。明示キーは非空の ordinal 一意値とし、省略時は通常の Element 対応付けに従う。配置失敗や公開前準備の失敗で受理重みを変えない。更新要求と配置の差分がない場合は公開済みの入力幾何の版を維持し、観測や描画だけで提示の受領確認を無効化しない。

境界は隣接する二領域の識別子を保持する。並べ替え、削除、非表示、無効化または向きの変更では進行中の入力を取消す。取消しは既に受理した配分を保ち、完了通知を出さない。保持された非表示タブ内の実体も配分を維持するが、破棄後の永続化を保証しない。

### 入力と観測

境界の通常ポインター入力だけを捕捉し、pane 本文の TextBox や ScrollView の入力を奪わない。一境界の操作中に別ポインターから開始しない。捕捉の終了通知が Up より先に来る既存規約を使い、終端入力の context を所有権の根拠とする。

主軸の矢印は SmallStep、Shift または Control 付きは LargeStep、Home と End は隣接領域の最小寸法が許す両端へ移動する。Alt と Meta は処理しない。キー反復は既存捕捉がある場合だけ継続する。変更要求は次の成功したレイアウト境界で幾何とともに受理し、OnChanged は変更時だけ、OnCommitted は正常な操作終了後のレイアウト境界で呼ぶ。同じ境界より前に次の操作が始まった場合、前の未公開の完了通知を新しい操作へ引き継がない。例外で受理値を戻さず、通知前に終端所有状態を解除する。

境界は実際の一つの要素としてフォーカス、入力、描画、Automation の同じ幾何を使う。装飾用の隠れた Button を追加しない。配置変更は通常の入力幾何、clip、スクロール viewport、IME 候補位置の更新へ伝播する。組み込みの境界色を使い、追加テンプレート基盤を導入しない。

SplitView と Separator の役割を診断 schema 13 で公開する。Separator の数値は隣接二領域の合計に対する先頭領域の比率で、0〜1 の連続値とする。既存の SetValue 操作を通常入力 lease と現時点の probe の検査で配送する。最小寸法による制限は同じ入力経路で適用する。入力領域を持たない内部境界は観測対象にしない。親の TestId を指定した場合は、その値に .divider. と境界番号を続けた TestId を実際の境界へ割り当て、入れ子でも個別に指定できる。schema 1〜12 の JSON 形状は維持し、新しい役割と数値能力を混入させない。型付きクライアントはすべての祖先 query と操作の最低版の最大値を要求する。

### 公開 API と契約の差分

```diff
 namespace Lumyte.UI
 {
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public partial class SplitView : Panel
+            {
+                // 初期は Horizontal。両軸の有限上限が必要。
+                public UiOrientation Orientation { get; set; }
+                // 初期4。有限非負。
+                public float DividerThickness { get; set; }
+                // 初期8。有限正、DividerThickness以上。
+                public float MinimumHitSize { get; set; }
+                // 初期8。有限正。
+                public float SmallStep { get; set; }
+                // 初期40。有限正。
+                public float LargeStep { get; set; }
+                public Action<IReadOnlyList<float>>? OnChanged { get; set; }
+                public Action<IReadOnlyList<float>>? OnCommitted { get; set; }
+            }
+        }
+    }
+    public static partial class UiParentDataFactories
+    {
+        // 元の Widget に保存する。対応する直接の通常親以外では拒否する。
+        public static Action<Compose.Definitions.Widget> Pane(
+            this Compose.Definitions.SplitViewFactory factory,
+            UiArgument<float> weight = default, UiArgument<float> minimum = default);
+    }
 }
```

## 検討した代替案

- 外部の split-pane 実装を組み込む: 既存の UI framework と別の所有権、入力、配置を変換する必要がある。既存の有限制約と捕捉サービスへ小さな部品を追加する。
- 各領域を専用 Widget で包む: 配置だけの要素と入力階層が増えるため、受動的な親設定を選ぶ。
- 最小寸法を常に優先する: 小さい親から描画と入力があふれるため、親を優先して同比率縮小する。

## 結果と影響

通常の子の同一性、編集状態とフォーカスを保持して領域を分割できる。利用者は有限領域を与え、必要なら通知を明示的な保存処理へ接続する。Docking、表のセル編集と OS ダイアログは対象外とする。

受入れ条件は縦横、入れ子、空、ゼロ、極端な有限値、最小寸法不足、一回の子配置、通常ポインターとキー、取消し、再構築失敗の旧状態保持、スクロールと編集欄の幾何更新とする。論理 Automation の検証は物理 OS のアクセシビリティ、IME または GPU 表示の保証ではない。
