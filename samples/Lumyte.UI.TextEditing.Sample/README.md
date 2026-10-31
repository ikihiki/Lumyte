# Text editing sample

Windows 10以降で実際のTSF・Window・Wgpu描画を接続するsampleです。

```sh
dotnet run --project samples/Lumyte.UI.TextEditing.Sample -- path/to/JapaneseFont.ttf --custom-candidates
```

fontは利用側で用意します。既定はSystem候補です。`--custom-candidates`は対応TIPだけでHosted候補表示を試します。TextBoxをクリックして日本語変換・候補移動・確定・取消、ドラッグ選択、clipboard、Alt+Tab、Window終了を確認してください。System復帰・GPU退役・session drainを終えてからPlatformを破棄します。Linux上ではビルドのみ検証でき、実OS IME試験の代用にはなりません。
