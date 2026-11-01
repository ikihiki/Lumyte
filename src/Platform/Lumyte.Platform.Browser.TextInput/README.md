# Lumyte.Platform.Browser.TextInput

```csharp
var service = await BrowserTextInputService.CreateAsync(platform);
```

`lumyte-text-input.js`を起動ページの相対URLへ配置します。visible canvasに対応する透明textareaを一つ用い、DOMのvalue/selectionを正本としてcomposition/inputを文書へ反映します。compositionendのdataを追記しません。候補表示はBrowserのSystem UIを使います。

通常frameの前にservice.Advance→UI入力配送→connection.SynchronizeFocusを実行します。`RegisterFocusBridge(dispatchInput, synchronizeFocus, canDispatch)`でpointer/TabのDOM callback内にも同じ処理を登録し、次のDOM入力より先に新editorへ接続します。canDispatchがfalseの間はその境界の入力を破棄し、遅延focusで文字を別editorへ流しません。

textareaはdisplay:none/offscreenにせずcaret近傍へ配置し、canvas↔proxyのfocus移動を同じWindowの論理focusとして扱います。blur/visibilitychange/canvas削除・終了時にsessionをdrainし、service.Disposeでlistener/proxyを除去します。physical IMEやmobile keyboardの差異は実機試験が必要です。
