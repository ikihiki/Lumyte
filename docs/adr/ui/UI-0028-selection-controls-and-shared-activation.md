# ADR-UI-0028: 選択・切替部品の値を表示実体で確定する

- 状態: 採用
- 日付: 2026-11-16
- 基準コミット: `76fe107f8919ce61b96a9a8d7b2fc0a23fcb47c7`
- 変更関係: 一部変更
- 変更元: [UI-0013](UI-0013-component-catalog-and-delivery-plan.md)の「コマンド・値入力」と「共通基盤と入力・見た目の分離」
- 変更対象: C3の値の正本、RadioButtonのkey、共通activation、意味情報と閉じた操作
- 継続する判断: 部品の区分、型付きComposition、単一所有ツリー、通常入力、任意パッケージの依存方向

## 背景

設定画面の作成者は、基準コードではButtonから切替値、混在状態、単一選択をそれぞれ実装する必要がある。同じ宣言を複数Surfaceで共有できるため、入力がWidgetの値を書き換えると独立した表示実体へ状態が漏れる。

[UI-0002](UI-0002-widget-tree-and-composition.md)の受動宣言、[UI-0005](UI-0005-pointer-dispatch-and-capture.md)と[UI-0006](UI-0006-focus-and-keyboard.md)のcapture、[UI-0019](UI-0019-signals-and-reactive-state.md)の一方向のモデル更新を維持する。基準コードの `src/UI/Lumyte.UI/ButtonElement.Input.cs` と `ButtonElement.Keyboard.cs` は一つの押下ownerと終端での最新callback取得を実装する。

## 決定

任意のLumyte.UI.ControlsにToggleButton、CheckBox、RadioButton、RadioGroup、Switchを追加する。宣言は意図を記述し、Elementが各接続寿命の受理済み操作値を保持することで、モデル書戻しの有無によらず通常入力を一貫させる。

### 利用者の変更前・変更後

変更前はButtonのcallbackでアプリのboolを反転できるが、checked意味情報や三状態巡回は利用者が実装する。

```csharp
using static Lumyte.UI.Compose;

bool sound = false;
var button = Button(onClick: () => sound = !sound);
```

```csharp
using static Lumyte.UI.Controls.Compose;

bool sound = false;
var toggle = Switch(isOn: sound, onChanged: value => sound = value);
// 次の明示的な構築結果でモデルの意図を再提示できる。
```

### 値の採用と通知

UI-0013の「Widgetの値を正本」は宣言・モデルによる意図の指定と解釈し、操作で宣言setterを呼ぶ契約にはしない。初回と次の明示的な宣言採用で指定値をElementへ採用する。同じ宣言参照を維持する子の更新やlayoutだけでは操作値を戻さない。新しい宣言を採用した場合は同じ指定値でも受理値を置き換え、アプリは値の拒否・変換を明示できる。

操作は有効値を先に確定し、描画を無効化し、値が変化した場合だけ現在の宣言からcallbackを一回取得して同期実行する。handlerなしでも値は残る。callback例外で値を戻さず、入力ownerはcallback前に解除する。callback内の再入は新しい操作とする。setter、再構築、項目削除による補正は通知しない。通常の宣言setterは表示更新ではなく、新しい宣言による再構築を必要とする。

Signal引数も通常の宣言採用を使う。callbackでモデルSignalへ書き戻す場合だけ別Surfaceへ値が伝わり、暗黙の双方向同期はしない。モデル値が変わらず再構築もなければ、受理済み操作値を維持する。同じ受動宣言を二つのSurfaceへ接続しても受理値と押下は独立する。

ToggleButtonはfalse/true、Switchはoff/onを反転する。CheckBoxはnullable boolで、ThreeState=falseならfalse→true→false、null→true、trueならfalse→true→null→falseとする。外部からのnullは二状態設定でも混在表示として受理する。

### 共通認識と内容の所有

Buttonのpointer/key認識を一つの公開認識器へ抽出し、ButtonとControlsが同じ状態機械を使う。Downでcaptureとfocus、Upで解放済み状態から確定、repeatで再開始しない。領域外、取消、focus喪失、無効・非表示・除去、callback置換と例外清掃は既存契約を維持する。geometryの検査はElementのprotected hookから通常probeと同じ判定を渡す。

内容と名前付き背景は通常のWidgetChildrenで一回初期化し、実行時slotへ接続する。内部の内容rootが必要なときは既存UiElement.BuildContentとUiElementContentを使い、第二の所有ツリーや戻り値Widgetの差替えは作らない。装飾markはownerの描画であり独立したfocus・Automation nodeではない。アプリが渡した内容の入力は通常の子優先・親への伝播を使い、TextBoxの編集targetを横取りしない。

すべてUiSurface.Dispatchを通し、TextInputScopeの登録を増やさない。[UI-0012](UI-0012-ime-and-text-editing.md)のIME優先、編集targetの遮断、同じscopeの一回配送を維持する。

### 単一選択とfocus

RadioButtonは継承したinit-onlyのWidget.Keyを選択識別子として使い、隠したmutable Keyを追加しない。直接の親はRadioGroupとし、group内で非空・ordinal一意のkeyを公開前の投影validatorで検証する。異なる親、重複、空keyでは旧ツリーを維持する。

選択値はRadioGroupElementだけが持ち、各RadioButtonは親のSelectedKeyとのordinal比較から選択を導く。初期と宣言採用のSelectedKeyが存在しなければnullへ補正し、削除・key変更でも存在しなくなった選択をnullへ戻す。無効・非表示になっただけでは選択を解除しない。選択済みradioの再操作は解除も通知もしない。

Tab候補は有効・可視・HitTestVisibleな選択済みradio、なければ先頭一つとする。候補は通常Children slotだけから取得し、Background slotのradioを含めない。左右・上下の修飾なし非repeat Downは有効な候補を宣言順に循環し、focusと選択を同じ操作で移動する。Space/Enterは共通認識のUpで選択する。並べ替えではkeyとElement identityを保持し、削除時のfocus清掃は既存Surfaceへ委ねる。

### 描画と依存

色は明示的な`SRgba<float>`、mark寸法と間隔はfinite・非負のstyle値とする。既定markはフォント不要の幾何で描き、任意の内容にMedia.Text等を渡せる。暗黙のfont load、テーマ、DI、Platform、global serviceを必要としない。

UI-0013のControls依存候補をC3の必要範囲へ限定する。ControlsはUIと必要な描画型だけを直接参照し、Mediaとmanaged TextEditingは設定画面で併用する任意依存とする。UI coreからControlsへの逆依存は追加しない。markの固定寸法はaspect依存なので共有rootのdestination capture再利用を許可せず、旧側をcaptureする。直接描画とretained描画は同じ本体座標とclipを使う。

### 意味情報と自動操作

snapshotへnullable checked、selected、group ID、option key、selected keyを追加し、混在をfalseへ潰さない。roleと操作capabilityは別であり、roleを指定するだけでCheck等を実行可能にしない。既存の値・進捗・profiling契約を維持する。

Check/Uncheckは望ましいbool状態、SelectOptionは望ましいgroupのkeyを指定する閉じた操作とする。現在値が一致すれば入力もcallbackも追加しない。変更が必要な場合はcapability・actionability・probeを確認する。Check/Uncheckは対象ID・望ましいbool・操作点を持つUiCheckInputを通常Dispatchへ一回送り、三状態でも目的値へ直接一回だけ確定・通知する。SelectOptionは対象radioへの通常pointer系列を使う。送信後にcallbackで状態が変わっても自動再送しない。望ましい状態のDispatchが拒否された場合は失敗し、自動再送しない。親menuへのCheckは通常pointer Downと同様に子menuを閉じ、親menuへ配送する。

遠隔DTOへ同じ値をコピーし、閉じたschema version 4へcheck/uncheck/select-optionとoptionKeyを追加する。version 1〜3の受付と既存フィールド制限を維持する。旧versionのsnapshot応答は専用source-generated DTOでC2の18 node fieldだけを出力し、新roleはnone、新操作capabilityは非公開とする。version 4では混在nullも含め五つの選択fieldを常に保持する。新clientはcatalogで対応を確認してから送る。操作はOverrideInput、AllowInput、既存control leaseを要求し、観測から入力へ昇格しない。[UI-0010](UI-0010-diagnostics-ui-automation.md)のUInt64 decimal string、有限座標、sequence、取消と再送禁止を維持する。

### 公開API差分

以下は基準ソースに対する主要な追加である。Widgetは`Lumyte.UI.Compose.Definitions.Widget`の略記、callbackは同期delegateである。Widget.Keyは変更しない。五宣言は`[Composable]`、値とcallbackは`[ComposeParameter]`を使い、通常の型付きfactoryとUiArgumentを生成する。内容は容量1、RadioGroupだけ複数の通常slotとし、各部品のBackgroundは名前付き複数slotとする。

各選択部品はMarkSize=18、Gap=6、Foreground=白、TrackColor=不透明なsRGB(0.2,0.3,0.5)を明示的な既定値とする。ToggleButtonはbool、CheckBoxはfalseを既定値とするnullable bool、Switchはoff、RadioGroupは選択なしで開始する。

```diff
+namespace Lumyte.UI.Controls
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public partial class ToggleButton : Widget
+            {
+                public bool IsChecked { get; set; } // 既定false、通知なし。
+                public Action<bool>? OnChanged { get; set; }
+            }
+            public partial class CheckBox : Widget
+            {
+                public bool? IsChecked { get; set; } // 既定false、nullは混在。
+                public bool ThreeState { get; set; } // 既定false。
+                public Action<bool?>? OnChanged { get; set; }
+            }
+            public partial class Switch : Widget
+            {
+                public bool IsOn { get; set; } // 既定false。
+                public Action<bool>? OnChanged { get; set; }
+            }
+            public partial class RadioGroup : Widget
+            {
+                public string? SelectedKey { get; set; } // 既定null。
+                public Action<string?>? OnChanged { get; set; }
+            }
+            public partial class RadioButton : Widget { } // 継承Keyが必須。
+        }
+    }
+}
+namespace Lumyte.UI
+{
+    // 同期入力の状態だけを所有し、入力contextを保持しない。
+    public sealed class UiActivationRecognizer
+    {
+        public bool IsPressed { get; }
+        public bool HasActivePress { get; }
+        public UiInputResult HandleInput(in UiInputContext context,
+            UiInputResult pointerDownDisposition, Action activate);
+        public void PointerCaptureChanged(UiPointerCaptureState state);
+    }
+    // finiteな操作点とcapability・現在のprobeをDispatchで検査する。
+    public sealed record UiCheckInput(UiAutomationId Target, bool IsChecked,
+        System.Numerics.Vector2 Position) : UiInput;
+    public partial class UiElement
+    {
+        protected virtual bool? AutomationChecked { get; }
+        protected virtual bool? AutomationSelected { get; }
+        protected virtual UiAutomationId? AutomationSelectionGroup { get; }
+        protected virtual string? AutomationOptionKey { get; }
+        protected virtual string? AutomationSelectedKey { get; }
+        // 通常Tab候補だけを制御する。pointerと明示focusの適格性は変えない。
+        protected virtual bool IsTabStop { get; }
+        protected bool IsInsideInputBody(System.Numerics.Vector2 localPosition);
+    }
+    public sealed class UiAutomationNode
+    {
+        public bool? IsChecked { get; }
+        public bool? IsSelected { get; }
+        public UiAutomationId? SelectionGroup { get; }
+        public string? OptionKey { get; }
+        public string? SelectedKey { get; }
+    }
+}
```

## 検討した代替案

- Widgetの値を入力で更新する: getterだけで現在値を読めるが、共有宣言と一方向Signal更新を破るため採用しない。
- callbackでモデルが更新されるまで値を変えない: モデルを唯一の値にできるが、handlerなしの切替が成立しないため、Elementの受理値を明示する。
- 五部品へButtonの状態機械をコピーする: 局所的には単純だが、取消・repeat・例外の互換性が分岐するため共通認識器を使う。
- radioごとにboolを保存する: 複数選択と親更新の補修が必要になるためgroupの一つのkeyから導く。

## 結果と影響

設定画面を通常の型付きfactory、with、内容slotで構築でき、通常入力と自動操作が同じ受理値を観測する。利用者は永続化・共有したい値だけをcallbackからモデルへ書き戻し、新しい宣言で外部の意図を採用させる責務を持つ。

受け入れ条件は五部品の生成factory、三状態巡回、通知前の値確定、handlerなし、最新handler、再入・例外、共有宣言の独立性、Signal更新、radioの投影検証失敗と並べ替え、focusと矢印移動とする。Buttonの既存capture/keyテスト、修飾・clip・非一様DPI・内容target・IMEの遮断、直接とretained/captureの等価性、snapshot・wire・権限・idempotent操作を検証する。Headless sampleとstrict NativeAOTを受け入れ対象とし、Windows実機、BrowserのOS IME、実GPUの確認は実行した環境ごとに区別する。
