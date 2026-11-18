# ADR-UI-0038: 部品が所有する表示領域を名前付き Slot で差し替える

- 状態: 採用
- 日付: 2026-11-18
- 基準コミット: `7bcf1bc256e226d04d7e2818893035be73ab9d2e`
- 変更関係: 一部変更
- 変更元: [UI-0031](UI-0031-numeric-input-and-accepted-values.md#決定)、[UI-0032](UI-0032-keyed-items-and-popup-selection.md#決定)、[UI-0025](UI-0025-borrowed-media-and-progress.md#決定)、[UI-0012](UI-0012-ime-and-text-editing.md#決定)
- 変更対象: 数値部品の装飾、項目生成の引数、進捗の表示領域、編集部品の背景、および宣言再構築時のテンプレート保持
- 継続する判断: 実体ごとの受理状態、キーと仮想化、編集文書と IME、明示的な借用資源、入力と表示ツリーの所有権

## 背景

画面の作成者は、Slider のノブや項目の表示を差し替えても、ドラッグ、選択、編集などの部品本来の動作を維持したい。基準コードでは Button の Background や TabItem の Header・Content は置き換えられるが、数値部品と進捗は矩形を直接描き、項目生成の Func は値以外の引数を受け取れない。

基準コミットの `src/UI/Lumyte.UI.Controls/SliderElementBase.cs`、`src/UI/Lumyte.UI.Collections/ItemsRepeaterElement.cs`、`src/UI/Lumyte.UI.Media/ProgressBarElement.cs`、`src/UI/Lumyte.UI.TextEditing/TextBoxElement.cs` を比較元とする。`src/UI/Lumyte.UI/WidgetChildren.cs` の一回限りの初期化と、`UiSurface.RebuildPlan.cs` の検証後の公開を維持し、Slot の宣言だけで実体への接続が完了するとは扱わない。

## 決定

部品の内部に意味のある表示領域へ名前付き Slot を設け、表示の生成と配置は所有する部品が担当する。動的な表示には引数付きの CompositionTemplate delegate、固定の表示には構築済みの Widget を使い、レイアウト専用部品や描画末端へ同じ機能を重複追加しない。

CompositionTemplate とその型付き Slot 入口を依存契約として使用する。UI はテンプレートの評価時期・引数・結果の接続を担当し、Composition 自体に UI の状態や所有権を追加しない。

### 利用者の変更前・変更後

変更前は Slider の値や色を設定し、ノブの内容は組み込み描画へ任せる。

```csharp
using Controls = Lumyte.UI.Controls.Compose;

var slider = Controls.Slider(min: 0, max: 100, step: 1, value: 25);
```

変更後は同じ部品のまま、ノブへラムダまたは固定の構成を渡す。以下ではアプリが借用する textLayoutProvider の取得と描画先への接続を省略する。Thumb に渡す Value は宣言の初期値ではなく、実体が受理した値である。

```csharp
using Lumyte.UI.Controls;
using Controls = Lumyte.UI.Controls.Compose;
using Media = Lumyte.UI.Media.Compose;

var slider = Controls.Slider(min: 0, max: 100, step: 1, value: 25)[
    Controls.Slider.Thumb()[
        thumb => Media.Text(
            value: thumb.Value >= 50 ? "◆" : "●",
            layoutProvider: textLayoutProvider)
    ]
];

var fixedSlider = Controls.Slider()[
    Controls.Slider.Thumb()[
        Media.Text(value: "◆", layoutProvider: textLayoutProvider)
    ]
];
```

項目は旧来の引数を維持したまま、値と受け取り側の宣言を別々の引数で使える。変更前は `itemTemplate: value => ...` とし、変更後は利用箇所へ次のように記述する。people、キー、provider の準備は省略する。

```csharp
using Lumyte.UI.Collections;
using Collections = Lumyte.UI.Collections.Compose;
using Media = Lumyte.UI.Media.Compose;

var list = Collections.ListBox<Person>(
    items: people, keySelector: person => person.Id)[
    Collections.ListBoxFactory<Person>().Item()[
        (person, owner) => Media.Text(
            value: $"{person.Name} / 全{owner.Items.Count}件",
            layoutProvider: textLayoutProvider)
    ]
];
```

### 部品が持つ状態と接続

Slider は Track・Thumb、RangeSlider は Track・Thumb・LowerThumb・UpperThumb を提供する。RangeSlider の個別のノブ指定は共通 Thumb より優先し、IsUpper で上下を識別する。RangeSlider.Track に渡す文脈は下側の受理値と IsUpper=false とする。テンプレートを省略した領域だけ従来の描画を使い、指定された領域の組み込み描画は抑制する。

ノブの位置とサイズは既存の幾何を使用し、子の希望サイズでトラックや入力範囲を変更しない。装飾は入力、フォーカス、自動操作の独立した対象にせず、元の部品が捕捉、キーボード操作、数値の制約、通知を担当する。テンプレートは初回接続、設定または受理値の変更に対する準備で評価し、描画処理中には評価しない。

実体が受理した値の変更は既存の操作として先に成立する。テンプレートの再評価に失敗しても、既に通知した操作を取り消したとは扱わない。一方、表示ツリーは提案全体の検証成功まで公開せず、失敗時に古いツリーを部分的に書き換えない。

ItemsRepeater・ListBox・ComboBox は Item を提供し、ComboBox は SelectedItem・Empty も提供する。指定された Slot は同じ役割の旧 Func 引数より優先し、省略時は旧引数を使う。既存のキー検証、項目ごとの新鮮で交わらない表示ツリー、選択と仮想化を維持する。ComboBox のポップアップ内でも owner は元の ComboBox 宣言であり、内部の ListBox を外へ漏らさない。

項目の owner は受け取り側の宣言であり、現在の選択やスクロールを持つ表示実体ではない。仮想化された場合も項目と元の owner を渡す。Composition が owner を自動注入するのではなく、UI の実体化処理が明示的に渡す。

ProgressBar は Track・Fill の固定構成を受け取り、Fill の配置幅を正規化された Value から決める。未指定は既定描画、明示的な空配列は対応する既定描画を隠す。TextBox と NumericBox は Background を受け取り、未指定・空の背景は透明のまま、文字・選択・キャレット・IME は編集実装が描く。これらの装飾も入力、フォーカス、自動操作から除外する。

### 評価、所有権、再構築

テンプレート Slot に渡す固定の構成は、記述した時点で作られ、入力を無視して同じ構成を返す delegate として扱う。自動複製しない。項目間、複数の画面、RangeSlider の上下など、異なる専有境界に同じテンプレート結果を取り付けようとした場合は所有権の検査で拒否する。複数箇所で新しい構成が必要ならラムダ内で構築する。WidgetChildren へ渡す静的な装飾は従来の宣言共有を維持し、画面ごとの表示実体は独立する。

構築後のテンプレート Slot と、空指定による既定描画の抑制など子配列以外の設定は、宣言の再構築レシピにも登録する。シグナルによる引数だけの更新でも Slot を失わず、新しい宣言へテンプレートを再適用する。再適用の関数は古い owner を捕捉せず、再構築先を引数として受け取る。with 内の指定は元のファクトリが再実行するため、別のレシピを累積させない。

受理値に追従する表示の再準備は実体単位で予約し、入力配送や描画の途中で子を付け替えない。所有する Surface の更新境界で予約を処理し、切断・廃棄済みの実体と古い設定への予約を破棄する。失敗は呼び出し側へ伝え、成功していない内容を公開しない。

### 公開 API と契約の差分

以下は基準コミットからの追加宣言の抜粋である。Widget は `Lumyte.UI.Compose.Definitions.Widget`、CompositionTemplate は `Lumyte.Composition` の型を表す。Slot の定義メソッドは部品内の非公開 static void で、生成入口は具体的な部品のファクトリへ追加する。

```diff
 namespace Lumyte.UI.Controls
 {
+    public readonly record struct SliderThumbContext(double Value);
+    public readonly record struct RangeSliderThumbContext(double Value, bool IsUpper);
     public static partial class Compose
     {
         public static partial class Definitions
         {
             public partial class Slider
             {
+                // null は未指定。評価時期と所有権は本文の契約に従う。
+                public CompositionTemplate<SliderThumbContext, Widget>? TrackTemplate { get; private set; }
+                public CompositionTemplate<SliderThumbContext, Widget>? ThumbTemplate { get; private set; }
             }
             public partial class RangeSlider
             {
+                public CompositionTemplate<RangeSliderThumbContext, Widget>? TrackTemplate { get; private set; }
+                public CompositionTemplate<RangeSliderThumbContext, Widget>? ThumbTemplate { get; private set; }
+                public CompositionTemplate<RangeSliderThumbContext, Widget>? LowerThumbTemplate { get; private set; }
+                public CompositionTemplate<RangeSliderThumbContext, Widget>? UpperThumbTemplate { get; private set; }
             }
         }
     }
 }
 namespace Lumyte.UI.Media
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
             public partial class ProgressBar
             {
+                // 容量はそれぞれ1。初期化は空も含め一回だけ。
+                public WidgetChildren TrackChildren { get; }
+                public WidgetChildren FillChildren { get; }
             }
         }
     }
 }
 namespace Lumyte.UI.TextEditing
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
             public partial class TextBox
             {
+                // NumericBox も同じ保存領域を継承する。容量1、入力を受けない。
+                public WidgetChildren BackgroundChildren { get; }
             }
         }
     }
 }
 namespace Lumyte.UI
 {
     public sealed class WidgetChildren
     {
-        internal bool IsInitialized { get; }
+        // 成功した初期化を示す。明示的な空配列でも true。
+        public bool IsInitialized { get; }
     }
     public partial class UiElement
     {
+        // 所有実体の内容再準備を次の安全な更新境界へ予約する。
+        protected void InvalidateContent();
+        // 現在の設定で self → children の順序を厳密に保つ場合だけ true。
+        protected virtual bool UsesDefaultRecordingOrder { get; }
     }
     public sealed class UiElementContent
     {
         public UiElementContent(Widget content, Action? onCommitted = null);
+        // ownsContent=true は未親子化の根と排他的な実体所有を要求する。
+        public UiElementContent(Widget content, bool ownsContent, Action? onCommitted = null);
     }
     public static partial class Compose
     {
         public static partial class Definitions
         {
             public abstract partial class Widget
             {
+                // 子配列以外の Slot 設定を再構築へ引き継ぐ。value と assign は非null。
+                protected void RegisterSlotState<TDeclaration, TState>(
+                    TState value, Action<TDeclaration, TState> assign)
+                    where TDeclaration : Widget;
+                // template と assign は非null。テンプレートは実行しない。
+                // assign は再構築先を使い、元の宣言を捕捉しないこと。
+                protected void RegisterTemplateSlot<TDeclaration, TTemplate>(
+                    TTemplate template, Action<TDeclaration, TTemplate> assign)
+                    where TDeclaration : Widget where TTemplate : Delegate;
             }
         }
     }
 }
```

生成入口の公開 API は次の形とする。下記は Slider と ListBox の代表的な宣言であり、RangeSlider の各部品と他のコレクションも対応する具体型を使う。

```diff
 namespace Lumyte.UI.Controls
 {
+    public static class ComposeSliderCompositionExtensions
+    {
+        // 入口とインデクサーではテンプレートを評価しない。
+        public static CompositionTemplateSlot<Compose.Definitions.Slider,
+            SliderThumbContext, Widget> Track(this Compose.Definitions.SliderFactory factory);
+        public static CompositionTemplateSlot<Compose.Definitions.Slider,
+            SliderThumbContext, Widget> Thumb(this Compose.Definitions.SliderFactory factory);
+    }
 }
 namespace Lumyte.UI.Collections
 {
+    public static class ComposeListBoxCompositionExtensions
+    {
+        // owner は評価時の ListBox 宣言。旧 itemTemplate 引数は維持する。
+        public static CompositionTemplateSlot<Compose.Definitions.ListBox<T>,
+            T, Compose.Definitions.ListBox<T>, Widget> Item<T>(
+            this Compose.Definitions.ListBoxFactory<T> factory) where T : notnull;
+    }
 }
```

Collections の旧 ItemTemplate・SelectedItemTemplate・EmptyTemplate は型と省略時の契約を変えない。新 Slot は `CompositionTemplate<T, ItemsRepeater<T>, Widget>`、`CompositionTemplate<T, ListBox<T>, Widget>`、`CompositionTemplate<T, ComboBox<T>, Widget>` を受け、ComboBox.Empty は `CompositionTemplate<ComboBox<T>, Widget>` を受ける。これらは delegate を部品内部の実体化処理へ渡す入口であり、表示の準備前に実行しない。

### 全 UI の適用判断

基準コードの具体的な公開 Widget 36 種を対象とし、名前の似たサービスやレイアウト修飾を Widget と数えない。追加・維持・対象外・見送りを区別する。対象外は別の役割で既に内容を表現できる場合、見送りは意味のある差し替え点があるが今回その実装を追加しない場合である。

| モジュール | 部品 | 判断と理由 |
| --- | --- | --- |
| UI | Button | 既存の通常内容と Background を維持する |
| UI | Panel | 通常内容が任意の子を表すため追加 Slot は対象外 |
| UI | Flex | Panel の内容と配置規則を維持し追加は対象外 |
| UI | Grid | 通常内容とセルの親データを維持し追加は対象外 |
| UI | Stack | 通常内容と順序配置を維持し追加は対象外 |
| UI | Wrap | 通常内容と折り返し配置を維持し追加は対象外 |
| UI | SizedBox | 既存の0〜1内容で足りるため追加は対象外 |
| UI | ScrollView | 内容が表示領域を表す。内蔵スクロールバーがないため追加は対象外 |
| UI | Rectangle | 描画末端であり内部領域の差し替えは対象外 |
| UI | Separator | 描画末端であり追加は対象外 |
| Controls | CheckBox | 既存内容・Backgroundを維持。チェック表示そのもののIndicatorは見送る |
| Controls | RadioButton | 既存内容・Backgroundを維持。選択マークのIndicatorは見送る |
| Controls | ToggleButton | 既存内容・Backgroundを維持する |
| Controls | Switch | 既存内容・Backgroundを維持。状態付きTrack・Thumbは見送る |
| Controls | RadioGroup | 直接の選択肢と既存Backgroundを維持する |
| Controls | Slider | Track・Thumbを追加する |
| Controls | RangeSlider | Track・Thumb・LowerThumb・UpperThumbを追加する |
| Controls | ScrollBar | Controllerに追従するTrack・Thumbは見送る |
| Controls | Expander | 既存Header・Contentを維持。開閉Indicatorは見送る |
| Controls | TabItem | 既存Header・Contentと選択時の表示を維持する |
| Controls | TabView | 直接のTabItemを維持。ヘッダー帯の追加装飾は見送る |
| Controls | Menu | 直接のMenuItemを維持。追加の面装飾は見送る |
| Controls | MenuItem | 既存内容とCreateSubmenuを維持。マークおよびSubmenu Slotは見送る |
| Controls | Tooltip | 既存アンカー内容・CreatePopupを維持。Popup Slotは見送る |
| Collections | ItemsRepeater | 値と自身を受けるItemを追加する |
| Collections | ListBox | 具体的なListBoxをownerに渡すItemを追加する |
| Collections | ComboBox | Item・SelectedItem・Emptyを追加する |
| Media | ProgressBar | 固定構成のTrack・Fillを追加する |
| Media | BusyIndicator | phaseに追従するIndicatorの接続は見送る |
| Media | Text | 文字組みを担う描画末端。追加は対象外 |
| Media | Image | 画像の描画末端。Placeholderの構成差し替えは見送る |
| Media | Icon | ソースに基づく描画末端。追加は対象外 |
| TextEditing | TextBox | Backgroundのみ追加し、編集の表示内容は置き換えない |
| TextEditing | NumericBox | TextBoxの背景を継承し、具体的なBackground入口を追加する |
| TextEditing | ImeCandidateView | Candidate・Footerの差し替えは見送る。入力範囲と候補の秘匿を維持する |
| Windowing | Popup | 独立Surfaceの既存Contentを維持。所有側の装飾子を増やしても代替にならない |

`src/UI` 外も確認し、`src/Maps/Lumyte.Maps.UI/MapView.cs` の MapView を追加で検討した。これは借用した MapSession を描画・操作する手書きの Widget で、内部 Widget の表示領域を持たない。地図描画自体の差し替えは対象外とし、Widget による操作パネルや地図オーバーレイの追加は、パン・ズーム・借用寿命を保つ別の接続契約が必要なため見送る。

抽象 Widget と UiFunctionWidget は共有契約であり具体部品に数えない。Padding・Border・Align・ConstrainedBox は表示 Widget ではなく既存の修飾操作である。Window は `src/UI/Lumyte.UI.Application/Compose.cs` の手書きファクトリで、root と content による独立表示を維持する。アプリケーション内蔵のタイトルバー・閉じるボタン・リサイズ領域は実際の UI だが、操作と別Surfaceの経路を伴うため今回の名前付き Slot 化は見送る。UiApplicationViewSlot は表示所有権の交換口であり Composition Slot と混同しない。

## 検討した代替案

- 全 Widget に同じ Overlay を追加する: API は揃うが、部品が直接描くノブや進捗の描画を置き換えず、Popup の独立表示にも届かない。部品ごとの意味と接続を定める。
- 通常内容の Content 別名を全コンテナーへ加える: 既存の表現と重複し、生成インデクサーや親子検証の互換性に影響する。既存の通常内容を維持する。
- 旧 Func 引数を削除する: 入口は一つになるが、既存のアプリを不必要に書き換える。Slot を優先する互換経路を追加する。
- 描画時にテンプレートを実行する: 値を取得しやすいが、描画中にツリーを構築し、副作用・失敗・所有権の境界が不明確になる。実体の準備と公開へ接続する。

## 結果と影響

利用者は操作を実装し直さず表示領域を差し替えられる。テンプレートは所有権に適合する構成を返し、文字や画像の借用資源を維持する必要がある。全 UI の検討と全ての差し替え点の実装完了は同じではなく、上表の見送りを汎用背景で完了扱いにしない。

検証では、具体的ファクトリからのラムダ推論と固定構成、未指定の既定描画、明示的な空、geometryとクリップ、受理値の追従、評価回数、入力・フォーカス・自動操作の除外、旧引数との優先順位を確認する。項目は通常・仮想化・選択表示・ポップアップの全経路と、再構築時のownerの同一性を確認する。

構成の重複やnull、テンプレート例外では公開前のツリーを維持し、通知済み操作の非ロールバックと区別する。複数Surface、接続解除、破棄、再入、失敗後の再試行、引数シグナルと構築レシピの反復も検証する。既存のUI、Controls、Collections、Media、TextEditing、Windowing、Automation、Applicationの回帰と、代表的なホストのNativeAOT・trimmingを受け入れ条件とする。
