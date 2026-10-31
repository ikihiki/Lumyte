# Text editing verification

```sh
dotnet test tests/UI/Lumyte.UI.TextEditing.Tests -c Release
node tests/Platform/browser-text-input.test.mjs
```

文書transaction・部分確定/取消・Unicode/grapheme、Parleyのbidi/ligature/UTF-16対応、TextBoxの描画・focus、Fillのlease・本文非公開、候補presentationの準備/操作/退役を確認します。Parleyのnative libraryと同梱font fixtureを使い、glyph幅からの推定で代用しません。

Browser試験はChromiumが必要です。環境変数CHROMEで実行ファイルを指定できます。実DOM上のcomposition/input/selection、A→Bの同期focus、旧キー終端、proxyの清掃をsynthetic eventで確認します。実OSの日本語IME、mobile keyboard、Windows COM/TIPの成功を意味しません。

Windowsのmanaged ACP fixtureは`tests/Platform/Lumyte.Platform.Windows.TextInput.Tests`で実行します。Windows実機の手順は[sample](../../../samples/Lumyte.UI.TextEditing.Sample/README.md)を参照してください。
