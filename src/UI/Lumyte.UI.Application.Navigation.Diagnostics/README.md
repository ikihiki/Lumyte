# Lumyte.UI.Application.Navigation.Diagnostics

Navigationの公開済みmetadataを、標準ApplicationのDiagnosticsへ追加する任意のpackageです。Navigationを使わないアプリは、従来どおり`Application.Diagnostics`だけで通常のUI操作を利用できます。

```csharp
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Navigation.Diagnostics;

builder.UseDiagnostics(options).UseNavigationDiagnostics();
```

`UseNavigationDiagnostics`は`UseDiagnostics`の後に一度だけ呼びます。通常のTarget交換は標準hostと基本Diagnosticsが処理し、この拡張はObserve操作`ui.navigation/state`だけを追加します。

必須の`window-context-id`へ空文字列を渡すと全navigation slot、GuidのD形式を渡すと一件を返します。静的なWindowは含めません。結果の`json`はschemaVersion 1の閉じた形式で、`UiNavigationStateSchema.Parse`で読み取れます。引数本文や編集文字列は返しません。
