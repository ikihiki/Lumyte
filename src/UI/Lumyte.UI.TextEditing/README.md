# Lumyte.UI.TextEditing

単一行TextBoxとフォーカスを奪わないImeCandidateViewをComposition関数で構築します。通常の利用では文字列を渡し、変更イベントでアプリの状態を更新します。文書・選択・IME変換状態はUIが所有します。

```csharp
using static Lumyte.UI.TextEditing.Compose;
var editor = TextBox(text: text, layoutProvider: provider,
    onTextChanged: change => text = change.Text);
```

`provider`は文字組み・visual caret・選択矩形を返す`IUiTextLayoutProvider`です。Parley実装は別packageです。OS接続はホストがSurfaceごとに`UiTextInputConnection`を一度作成します。配下のTextBoxは所属Surfaceから入力handlerを自動解決するため、構築引数やプロパティに入力システムを指定しません。独自ホストは`UiTextInputScope.Attach(surface, handler)`の登録を所有できます。登録はSurfaceごとに一つで、異なるWindow/Surfaceへ共有されず、登録のDisposeで解除されます。

宣言プロパティは構築時に設定します。文書更新は専用のconnection反映経路でlayoutを無効化します。OnTextChangedは確定値だけ、OnCompositionChangedはpreeditの変更をframeで通知します。TextBoxElementの入力はElementの共通hookで処理し、Buttonの内側でも親のClickへ透過しません。本文は既定のAutomation snapshotに入りません。候補viewはmetadataと子孫もredactし、一般Click/Fill対象にしません。

低水準ホストで状態を反映する場合は、構築関数を再実行して`surface.Rebuild(Build())`を呼びます。新しい宣言は次のUpdateLayoutで適用します。同じ親・子slot内の型とkey（省略時は位置と型）が一致すると、内部文書・選択・水平offset・focus・継続中のIME sessionを保持します。並べ替えるeditorには`key: "name"`のような安定keyを指定します。

同じ確定値の再提示はpreeditやcaretを変更しません。異なる文字列は外部値の更新として反映し、composition中なら既存sessionを取消・退役させてから適用します。外部値の反映はOnTextChangedへechoしません。入力を拒否・変換する場合も、callback後に採用する文字列で再構築します。省略した再構築をライブラリが任意のモデル変数から推測することはありません。Signalを使った自動再構築は後続の統合対象です。

`TextBoxElement.Document`はmount後の低水準の編集backend・Automation統合用のget-only参照です。TextBoxへ文書を引数として渡したり置換したりせず、通常のアプリ状態には`Text`と`OnTextChanged`を使用します。

## NumericBox

NumericBoxはTextBoxを継承し、同じElement・TextDocument・明示的なLayoutProvider・IME接続・Fillを使用する。宣言はValueを指定し、Textは数値表示の入力に使用しない。Cultureは不変のsnapshot、表示はR形式、parseはNumberStyles.Float（桁区切りなし）である。空はnull、不完全な符号・小数・指数や不正値は文書に保持して数値を変更しない。IsValidは本文を公開せずにAutomationのNumeric情報へ反映する。

OnChangedは妥当な数値を受理した後、OnCommittedは非変換中のEnterまたはfocus喪失で妥当な編集を整えた後に通知する。Escapeは最後の受理値の表示へ戻し、新宣言の採用は既存sessionの取消経路から文書へ反映する。[判断と契約](../../../docs/adr/ui/UI-0031-numeric-input-and-accepted-values.md)を参照する。

## 編集領域の背景 Slot

TextBox と NumericBox の各ファクトリで Background Slot を使用できます。容量は1、未指定と空配列は透明です。背景は文字・選択・キャレットより先に描画し、文書、IME、入力、フォーカス、自動操作の所有者は編集部品のままです。

```csharp
TextBox(layoutProvider: provider)[
    TextBox.Background()[Rectangle(fill: backgroundColor)]
]
```

NumericBox.Background は継承した同じ背景保存領域を使います。IME候補の行やフッターのテンプレート化はこの変更には含めません。
