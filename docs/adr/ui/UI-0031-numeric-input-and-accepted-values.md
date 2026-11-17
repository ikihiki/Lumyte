# ADR-UI-0031: 数値の受理値と編集中の文字列を分ける

- 状態: 採用
- 日付: 2026-11-16
- 基準コミット: `97accb2ca833a173b88855822b2cd63b8c973c96`
- 変更関係: 一部変更
- 変更元: [UI-0013](UI-0013-component-catalog-and-delivery-plan.md#コマンド値入力)のC6と共通基盤
- 変更対象: 数値入力の値の正本、二つのつまみの識別、NumericBoxの配置assembly、自動操作と数値の意味情報
- 継続する判断: 部品区分、通常入力、型付きComposition、一つの所有ツリー、明示的なフォントと任意packageの依存方向

## 背景

設定画面の作成者は、数値のドラッグと文字編集を同じ数値モデルへ接続したい。編集中の符号や指数、IMEの未確定文字は数値に変換できず、入力途中の文書を数値で上書きすると編集を続けられない。

基準コードの `src/UI/Lumyte.UI.TextEditing/TextBoxElement.cs` は表示実体ごとに文書、選択、変換中の状態を持ち、`src/UI/Lumyte.UI.Windowing.TextInput/UiTextInputConnection.cs` はその実体を唯一の入力先として登録する。[UI-0028の値の採用](UI-0028-selection-controls-and-shared-activation.md#値の採用と通知)は、操作で宣言を変更せず表示実体へ値を先に受理する。

## 決定

SliderとRangeSliderを任意のControlsへ、NumericBoxを任意のTextEditingへ追加する。三部品は表示実体ごとの受理済み数値を持ち、NumericBoxだけは同じ実体のTextBox文書を編集用の文字列として併せ持つ。数値の確定と文字入力の確定を別の通知にすることで、利用側は明示的にモデルへ書き戻せる。

### 利用者の変更前・変更後

変更前には専用の数値入力はない。TextBoxで文字を受け取ってから、利用者が自分で数値変換と途中の文字列の保持を行う。

```csharp
using System.Globalization;
using Lumyte.UI.TextEditing;
using static Lumyte.UI.TextEditing.Compose;

// providerは利用者がフォントを供給して所有する。
var before = TextBox(layoutProvider: provider, text: "1",
    onTextChanged: change => double.TryParse(change.Text,
        NumberStyles.Float, CultureInfo.InvariantCulture, out _));
```

変更後は通常の引数、with、内容の構築規則を維持して値と通知を指定する。providerの寿命管理とモデルの初期化は例から省略する。

```csharp
using System.Globalization;
using Lumyte.UI;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using static Lumyte.UI.TextEditing.Compose;

var after = Stack()[
    Slider(min: 0, max: 100, step: 1, value: 20,
        onChanged: value => volume = value),
    RangeSlider(min: 0, max: 100, value: new Lumyte.UI.Controls.SliderRange(20, 80)),
    NumericBox(value: 20, min: 0, max: 100,
        culture: CultureInfo.InvariantCulture, layoutProvider: provider,
        with: [Padding.All(4)])];
```

### 受理値と宣言の採用

初回と新しい宣言の採用時に指定値を表示実体へ静かに採用する。同じ宣言参照の維持や配置だけでは操作値を戻さず、新しい宣言なら指定値が以前と等しくても操作値を置き換える。Signalは既存の宣言再構築で更新し、入力からSignalへの暗黙の書戻しを行わない。同じ宣言を二つのSurfaceで共有しても、数値、文書、選択、captureは共有しない。

操作では数値を先に受理し、描画を無効化してから、最新のOnChangedを実際に値が変わった場合だけ一回呼ぶ。RangeSliderの二値は一つの不変なSliderRangeで同時に受理する。callback例外では受理値を戻さず、通常のSurface例外清掃でcaptureを解除する。再入や宣言の差替えは別の操作・採用として扱う。宣言検証と準備では利用側callbackを呼ばず、不正な宣言は公開前に拒否して旧ツリーを保つ。

### 一つまたは二つのつまみ

MinとMaxとValueは有限、MinはMax以下、Stepは有限かつ正とする。指定値は範囲へ制限し、利用者による操作だけMinを原点とするStepの格子へ丸める。ちょうど中間は大きい値側へ丸め、MinとMaxそのものは格子の端でなくてもそのまま到達できる。Min=Maxなら固定値として扱う。範囲差、格子index、重みの和を直接作らず、最大有限値から最小の正の値までNaNや無限を受理値にしない。

Sliderは既定で横向き160×24、RangeSliderも同じ大きさとし、親の制約へ制限する。縦向きは24×160である。横は右、縦は下へ増え、IsReversedで向きを反転する。つまみは所有者の幾何描画であり、内部Button、追加focus、追加の意味情報IDを作らない。

pointer Downでfocusと一つのcaptureを取得する。描画したつまみを押した位置のずれを保ち、3論理pixel以上移動したときからドラッグ値を更新し、移動しないtrackクリックではUpの位置を受理して操作を完了する。正常なUpは変更のなかった操作もOnCommittedを一回呼ぶ。Cancel、Escape、focus喪失、無効化、宣言再採用、除去、capture取消では最後の受理値を保ち、OnCommittedを呼ばない。SurfaceはUpの前にcapture喪失を通知するので、終端配送のcontext.IsCapturedを使用する。

修飾なしの主軸の矢印はStep、PageUpとPageDownは10 Step、HomeとEndは数値のMinとMaxへ移動する。一つのkey系列をcaptureし、repeatは同じ操作の値更新、Upは一回の完了とする。Escapeで取り消した後のUpは確定しない。

RangeSliderはLower以下へUpperを移動せず、Upper以上へLowerを移動しない。二つのつまみの意味は交換しない。pointerは近い方を選び、等距離なら現在選択した方を維持する。既定の選択はLowerで、Spaceは操作がないときに選択するつまみだけを切り替える。外側のTab候補は一つの所有者だけなので、重なったつまみでもSpaceと矢印で両方へ到達できる。

[UI-0030の仲裁](UI-0030-scroll-viewports-and-borrowed-controller.md#入力と表示領域への移動)へPreservePointerGestureで参加し、祖先ScrollViewのtouch panがドラッグを横取りしない。配置、clip、本体の入力座標は[UI-0024](UI-0024-ordered-layout-and-decoration-modifiers.md#描画入力automationの幾何)を共用する。

### 数値編集と文字入力

NumericBoxの宣言はTextBoxを継承し、実体もTextBoxElementを継承する。実際のTextDocument、layout provider、選択、caret、IME登録、Fillを同じ所有者で再利用する。TextEditingからControlsやPlatformへの参照、二つ目の編集用子や入力scopeを追加しない。TextBoxへ追加する保護された編集通知hookは、既存connectionが同じframe反映境界で呼び、通常のTextBox callback動作を維持する。

Cultureの既定値はInvariantCultureとし、setterでcloneして読み取り専用にする。NumberStyles.Floatで符号、小数、指数、前後の空白を受け入れ、桁区切りは受け入れない。表示は同じcultureとR形式を使い、暗黙のCurrentCultureや任意の丸めformatを使わない。空文字列は妥当なnull値、符号だけ、小数点で終わる文字列、指数途中、不正形式、NaN、Infinity、overflow、範囲外は不正な編集途中の文字列とする。不正文字列は文書に残し、受理値を変更しない。数値として等しい正負の0は変更通知を増やさない。

preeditは数値を受理せず、IMEの確定文書を通常のconnectionが反映した境界で数値を検査する。IMEが消費したEnterをNumericBoxが再捕捉しない。compositionがないときのEnterとfocus喪失だけが、妥当な編集をOnCommittedへ通知して受理値のR形式に整える。無効化・除去・宣言採用・Surface解放によるfocus取消はユーザーの確定と区別し、古い文書の確定通知を行わない。不正な編集は確定通知せず保持する。Escapeは最後の受理値の表示へ戻し、通知しない。読み取り専用と無効状態では編集しない。

新しい宣言は数値の外部採用として編集途中も置き換える。変換中なら既存connectionのsession取消と解放を先に行い、同じ実体の文書へApplication由来で反映する。文書を宣言へ保存せず、外部の共有TextDocumentを受け取る別入口を作らない。LayoutProviderとfontは利用側が明示供給し、借用寿命を保つ。providerなしの制約はTextBoxと同じである。

Headlessでは既定のOS文字入力機能を増やさず、明示したTextInputServiceとTextInputTargetsの組だけを借用できるようにする。利用側がbackendを所有し、driverは通常のframeと同じ順序でAdvanceとkey配送を行う。OS IMEの代替ではなく、同じapplication leaseと文書経路の再現用である。

### 意味情報と閉じた自動操作

既存snapshotのValueは有限な0〜1の意味を維持し、Sliderだけは範囲内の比率として公開する。実際のdoubleをValueへ流用せず、別の不変なNumeric情報でMin、Max、Step、受理値、Lower、Upper、IsValidを公開する。NumericBoxの不正な編集内容は公開せず、妥当性だけを公開する。RedactedSubtreeではNumericも読み取らずnullとし、既存の文字列検査許可を迂回しない。

SetValueとSetRangeはcapabilityを持つSlider所有者だけに対する閉じた操作とする。roleの指定は権限にならない。通常の排他的入力leaseを取得し、取得後の現在snapshot、操作可能領域、通常probe、popupとwindowの配送範囲を確認して、対象IDと有限値を持つUiSetNumericInputを一回だけDispatchする。Rangeは二値を同時に制限・丸めて受理する。配送後にcallbackが変更しても再送しない。NumericBoxは既存のFillと文字列検査許可を使い、数値setterによる文書の迂回を提供しない。

リモートschema 6にだけ数値commandとNumeric情報を追加する。Observeから操作へ昇格せず、OverrideInput、AllowInput、既存control leaseを要求する。v1〜v3は従来の18項目、v4とv5は選択情報を含む23項目の専用DTOへ投影し、新しいroleと操作を旧clientへ公開しない。有限値検査はJSONの受付にも適用し、未知・重複field、旧versionの新field、文字列NaN、数値overflowを拒否する。

SetValueとSetRangeの閉じた操作はドラッグ・key系列とは別の原子的な操作であり、値が変わった場合のOnChangedだけを通知する。OnCommittedは前節のpointer/key操作と数値編集の確定に限定する。すでに同じ値なら入力を追加しない。

### 公開APIの差分

以下は基準ソースへの主要な追加である。値とcallbackはComposeParameterによって型付きfactoryへ公開し、Widgetの既存keyとwithをそのまま使用する。

```diff
+namespace Lumyte.UI.Controls
+{
+    public readonly record struct SliderRange(double Lower, double Upper);
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public partial class Slider : Lumyte.UI.Compose.Definitions.Widget
+            {
+                public Slider(); // focus可能、横向き。
+                public double Min { get; set; } // 既定0、有限。
+                public double Max { get; set; } // 既定1、有限、Min以上。
+                public double Step { get; set; } // 既定0.01、有限正。
+                public double Value { get; set; } // 既定0、採用時にclamp、通知なし。
+                public UiOrientation Orientation { get; set; }
+                public bool IsReversed { get; set; }
+                public Action<double>? OnChanged { get; set; }
+                public Action<double>? OnCommitted { get; set; }
+            }
+            public partial class RangeSlider : Lumyte.UI.Compose.Definitions.Widget
+            {
+                public RangeSlider(); // 同じ範囲・step・向き・色の設定。
+                public SliderRange Value { get; set; } // 既定(0,1)、有限、Lower<=Upper。
+                public Action<SliderRange>? OnChanged { get; set; }
+                public Action<SliderRange>? OnCommitted { get; set; }
+            }
+        }
+    }
+}
+namespace Lumyte.UI.TextEditing
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public partial class NumericBox : TextBox
+            {
+                public NumericBox();
+                public double? Value { get; set; } // 既定null、有限かつ範囲内。
+                public double Min { get; set; } // 既定-double.MaxValue。
+                public double Max { get; set; } // 既定double.MaxValue。
+                public System.Globalization.CultureInfo Culture { get; set; }
+                public Action<double?>? OnChanged { get; set; }
+                public Action<double?>? OnCommitted { get; set; }
+            }
+        }
+    }
     public class TextBoxElement
     {
+        // 同じ実体の文書と既存の反映境界を使う。
+        protected virtual string DeclaredText { get; }
+        protected virtual void OnCommittedTextChanged(UiTextChange change);
+        protected void ReplaceApplicationText(string text);
+    }
+}
+namespace Lumyte.UI
+{
+    public sealed record UiNumericState(double Minimum, double Maximum, double? Step,
+        double? Value, double? Lower, double? Upper, bool IsValid = true);
+    public sealed record UiSetNumericInput(UiAutomationId Target, double Value,
+        double? Upper, System.Numerics.Vector2 Position) : UiInput;
     public sealed class UiAutomationNode
     {
+        public UiNumericState? Numeric { get; }
     }
     public readonly struct UiInputContext
     {
+        // 自分が所有する系列だけを取消し、他のownerには作用しない。
+        public bool ReleasePointerCapture(long pointerId);
+        public bool ReleaseKeyCapture(Lumyte.Input.Abstractions.Key key);
     }
+}
+namespace Lumyte.UI.Application.Headless
+{
     public sealed record UiHeadlessApplicationOptions
     {
+        // 既定null、組で指定し、applicationはDisposeしない。
+        public Lumyte.Platform.TextInput.Abstractions.ITextInputService? TextInputService { get; init; }
+        public Lumyte.UI.Windowing.TextInput.IUiTextInputTargetResolver? TextInputTargets { get; init; }
     }
+}
+namespace Lumyte.UI.Automation
+{
+    public sealed partial class UiLocator
+    {
+        public System.Threading.Tasks.Task SetValueAsync(double value,
+            UiWaitOptions? options = null, System.Threading.CancellationToken cancellationToken = default);
+        public System.Threading.Tasks.Task SetRangeAsync(double lower, double upper,
+            UiWaitOptions? options = null, System.Threading.CancellationToken cancellationToken = default);
+    }
+}
```

TrackColorは不透明なsRGB(0.2,0.3,0.5)、ThumbColorは白を既定とする明示色である。暗黙のthemeやfont providerを導入しない。

## 検討した代替案

- 数値parserの外部frameworkを導入する: 任意formatを増やせるが、有限doubleと明示cultureは既存のTryParseで足りるため依存を増やさない。
- NumericBoxをControlsへ置いて文字列だけを描く: packageはまとまるが、選択とIMEの二重実装になるためTextEditingの実際のeditorを使う。
- WidgetのValueを操作から変更する: 宣言getterは単純になるが、複数Surfaceと一方向Signal更新の契約を破るため表示実体へ保持する。
- つまみごとにButtonを置く: focusの実装は流用できるが、入力ownerと意味情報を増やすため一つの実体内で識別する。
- 既存Valueへ実数を格納する: DTOの変更は少ないが、進捗の正規化契約と厳密な旧readerを破るためversion付きの別情報を追加する。

## 結果と影響

画面の作成者は、途中の編集を壊さずに数値の変更と操作の完了を区別できる。ControlsはTextEditingへの依存を持たず、NumericBoxを使う画面だけが実際の文字layoutと入力backendを用意する責務を持つ。

受け入れ条件は有限値の端、等しい範囲、巨大な範囲とstep、つまみの交差制限、pointerとkeyの取消、通知の順序、最新handler、例外と再入、新宣言採用、Signal、共有宣言の独立性とする。NumericBoxでは実providerと文書、途中の符号・小数・指数、culture、範囲外、preedit、Enter、focus喪失、外部採用とsession終了を確認する。通常の入力leaseから三部品を操作するHeadless sample、厳密なNativeAOT、旧wire形状、権限とlive probeを検証する。

Headlessでの編集backend検証はWindows TSF、BrowserのOS IME、物理keyboard、実GPUの確認を代替しない。これらは実行環境ごとの別の確認範囲である。
