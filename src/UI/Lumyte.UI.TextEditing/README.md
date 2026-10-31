# Lumyte.UI.TextEditing

単一行TextBoxとフォーカスを奪わないImeCandidateViewをComposition関数で構築します。通常の利用では文字列を渡し、変更イベントでアプリの状態を更新します。文書・選択・IME変換状態はUIが所有します。

```csharp
using static Lumyte.UI.TextEditing.Compose;
var editor = TextBox(text: text, layoutProvider: provider,
    onTextChanged: change => text = change.Text);
```

`provider`は文字組み・visual caret・選択矩形を返す`IUiTextLayoutProvider`です。Parley実装は別packageです。OS接続はホストがSurfaceごとに`UiTextInputConnection`を一度作成します。配下のTextBoxは所属Surfaceから入力handlerを自動解決するため、構築引数やプロパティに入力システムを指定しません。独自ホストは`UiTextInputScope.Attach(surface, handler)`の登録を所有できます。登録はSurfaceごとに一つで、異なるWindow/Surfaceへ共有されず、登録のDisposeで解除されます。

宣言プロパティは構築時に設定します。文書更新は専用のconnection反映経路でlayoutを無効化します。OnTextChangedは確定値だけ、OnCompositionChangedはpreeditの変更をframeで通知します。TextBoxの入力はWidgetの共通hookで処理し、Buttonの内側でも親のClickへ透過しません。本文は既定のAutomation snapshotに入りません。候補viewはmetadataと子孫もredactし、一般Click/Fill対象にしません。

低水準ホストで状態を反映する場合は、構築関数を再実行して`surface.Rebuild(Build())`を呼びます。新しい宣言は次のUpdateLayoutで適用します。同じ親・子slot内の型とkey（省略時は位置と型）が一致すると、内部文書・選択・水平offset・focus・継続中のIME sessionを保持します。並べ替えるeditorには`key: "name"`のような安定keyを指定します。

同じ確定値の再提示はpreeditやcaretを変更しません。異なる文字列は外部値の更新として反映し、composition中なら既存sessionを取消・退役させてから適用します。外部値の反映はOnTextChangedへechoしません。入力を拒否・変換する場合も、callback後に採用する文字列で再構築します。省略した再構築をライブラリが任意のモデル変数から推測することはありません。Signalを使った自動再構築は後続の統合対象です。

`Document`は低水準の編集backend・Automation統合用のget-only参照です。TextBoxへ文書を引数として渡したり置換したりせず、通常のアプリ状態には`Text`と`OnTextChanged`を使用します。
