# Lumyte.Platform.Windows

Windows 10 version 1809 以降の Native Window、論理キーボード・マウス、Unicode Clipboard を提供します。STA スレッドで WindowsPlatform を一つ生成し、同じスレッドで使用します。

```csharp
using Lumyte.Platform.Windows;
using Lumyte.Windowing.Abstractions;

using var platform = new WindowsPlatform();
using IWindow window = platform.CreateWindow(new WindowDescriptor { Title = "Lumyte" });
window.Show();
platform.PumpEvents();

// Framework は具象クラスから表示先を借用する。
var native = (WindowsWindow)window;
using IDisposable retained = window.RetainNativeTarget();
nint hwnd = native.Hwnd;
nint instance = native.HInstance;
// Surface / Swapchain を既存 Graphics API で生成し、GPU 完了と解放後に retained を解除する。
```

`WindowsWindow` は `IWindow` を直接実装します。単一 Window に Windowing 管理実装は不要です。所有者付き Window、非活性表示、アンカー配置、通常・最小化・最大化、DPI の観測を提供します。Fullscreen は未対応です。`IsQuitRequested` は WM_QUIT の受信を示します。

CreateInputSource は論理キーボードとマウスを登録し、PumpEvents が集めた入力を排出します。Raw Input による物理デバイス分離、ゲームパッド、タッチ、ペンは未実装です。

Clipboard は Unicode テキストを読み書きし、内容を含まない変更通知を提供します。

Graphics と入力管理を先に終了してから Window を解放し、最後に Platform を解放します。通常の入力管理では InputSystem を先に終了し、その後に Source を Dispose します。

[単一 Window サンプル](../../../samples/Lumyte.Platform.Windows.Sample/README.md)と[設計](../../../docs/adr/platform/PLATFORM-0001-platform-and-window-contracts.md)を参照してください。

標準のCreateInputSourceは`IWindowInputSource`／`IOrderedInputSource`を実装します。InputSystemへsource全体を注入し、device.DrainEventsへ落としてWindow情報を失わないようにします。TryGetInputWindowIdは生存中のWindowIdを一回の入力接続IDへ解決します。mouse captureは取得中に開始し、buttonイベントと同時点の位置を同じ原因IDで記録します。capture喪失はMouseButtonCanceledData、Windowの終了はWindowInputClosedDataとして記録します。
