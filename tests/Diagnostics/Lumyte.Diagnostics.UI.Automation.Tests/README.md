# UI Automation Diagnosticsの検証

```sh
dotnet test tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests -c Release
```

閉じたJSON schema、権限・Actor/Sessionの分離、owner上の受付とAdvance、期限、Revision、Sequence再配送、Forget後の再実行禁止、artifact固定・範囲・保持・容量、Screenshot取消中の寿命を確認します。

LoopbackHandlerは別owner threadで通常のUI Dispatchを実行し、外側のDiagnostics JSONと型付きclientを使います。応答喪失時の同一要求再送、結果不明時の入力再利用禁止、hash不一致、catalog不一致も確認します。実transportの検証とは区別します。

[別プロセスHTTP／MagicOnionと実PNG](../../../samples/Lumyte.Diagnostics.UI.Automation.Game.Sample/README.md)／[実BrowserのHTTP Agent](../../../samples/Lumyte.UI.Browser.Sample/README.md)
