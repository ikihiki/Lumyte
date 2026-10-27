# Lumyte.Platform.Browser

既存 HTMLCanvasElement を一つの提示先として使う .NET 10 Browser バックエンドです。JavaScript モジュール `wwwroot/lumyte-windowing.js` をアプリの公開パスへ配置してください。

```csharp
using System.Runtime.InteropServices.JavaScript;
using Lumyte.Platform.Browser;
using Lumyte.Windowing.Abstractions;

// canvas は構成側が取得・所有する JSObject。
using var platform = await BrowserPlatform.CreateAsync(canvas, "./lumyte-windowing.js");
using IWindow window = platform.CreateWindow(new WindowDescriptor());
var native = (BrowserWindow)window;
JSObject context = native.CanvasContext;
// 既存 Browser Graphics の CreateSurface(context) へ渡す。
```

CanvasContext は借用で、Surface が使用する間は Native の保持トークンを保持してください。解放時はイベント購読を解除し、元の canvas 属性・寸法を復元します。構成側が借用中の canvas を削除すると Window は Closed / 要求失敗を報告します。

表示・非表示、寸法・DPI、フォーカス、DOM の論理キーボード・マウスを提供します。独立画面、Native 子 Window、fullscreen、DOM 文字入力は未対応です。ポップアップやメニューには任意の Windowing で Hosted 領域を明示生成します。

`Show` は `WindowActivation.Activate` の場合に canvas のフォーカスを要求します。
`PreserveFocus` の場合は既存のフォーカスを維持します。要求後の実際の活性状態は `State.IsActive` で確認します。

Clipboard は secure context の navigator.clipboard を使用し、非同期読み書きを提供します。現在の権限は Features とは別で、権限拒否は呼び出しの失敗として伝えます。変更通知は未対応です。

C# API は対応する JS context で呼びます。PumpEvents は DOM の観測・入力キューを一度だけ取得し、Input Source はその後にデータを排出します。マウスは canvas の論理座標、ホイールは deltaMode に応じた正規化値です。
