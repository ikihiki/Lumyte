# Lumyte.UI.Automation

LocatorによるUI検索、通常入力を使う操作、frameごとに再試行するassertionを提供する任意パッケージです。UI本体からこのパッケージへ依存しません。

```csharp
var page = new UiAutomationPage(host);
await page.GetByRole(UiRole.Button, name: "保存").ClickAsync();
await page.Expect(page.GetByTestId("saved-indicator")).ToBeVisibleAsync();
await page.GetByTestId("save").PressAsync(Key.Enter);
```

hostは一つのUiSurfaceを借用し、UI配送、新frameへの進行、入力の排他と後処理を実装します。通常入力の取得経路と自動操作は区別してください。OSやDOM入力を模倣するAPIではありません。ScreenshotAsyncはIUiAutomationScreenshotHostを実装したhostだけが使用できます。

`using Lumyte.UI; using Lumyte.UI.Automation; using Lumyte.Input.Abstractions;`を使用します。TestIdやAutomationNameは`using static Lumyte.UI.Compose;`による通常の構築引数です。

LocatorはWidget参照やIDを固定せず、毎回最新のsnapshotから検索します。GetByTestIdは非表示を含むordinal完全一致、GetByRoleは既定で非表示を除外します。連結したLocatorは現在の一致の子孫だけを検索し、重なるscopeを重複計数しません。単一要素の操作とassertionは複数一致ですぐ失敗し、0件は待機します。ToBeHiddenだけは0件でも成功します。

Clickは二つの連続frameでID・Bounds・ClippedBoundsが安定し、可視・有効・clip内の点が通常入力を受け取れることを確認します。既定はclipの中央、PositionはBounds左上からのoffsetです。遮蔽時の点の自動探索、強制クリック、scroll、Fillはありません。RoleをButtonにしただけではClick capabilityは付きません。

FocusとPressは通常のTabでfocusし、Pressでは物理修飾キーと指定キーのDown/Upを送ります。入力lease取得後に対象を再確認し、ClickのMove/Down/Upは一回の同期配送で完結します。最初の入力を送った後にcallbackが失敗しても再送しません。成功は入力系列の完了であり、アプリ処理の完了はExpect等で検証してください。

待機はhostのTimeProviderによる一つの期限を、入力予約・focus手順・再検査で共有します。既定5秒、UiWaitOptions.Timeoutで変更できます。期限は正でBCL timerの対応範囲内とし、無限待機は提供しません。NextFrameAsyncは実際の新しいframeを進めるhostの契約です。headlessでは仮想時計も一緒に進められます。

同じpageの同時操作はInvalidOperationException、別pageのLocatorをExpectへ渡すとArgumentExceptionです。別pageは独立ですが入力はhostのleaseで直列化します。hostでsurfaceを交換する場合は新しいpageを作ります。UiAutomationExceptionは分類、最後の不変snapshot、最大64件のtraceを持ち、callback例外はInnerExceptionへ保持します。callerの取消はOperationCanceledExceptionで、Data["Lumyte.UI.Automation.Trace"]に送信履歴を残します。

hostのleaseはDispatchで送信前に入力を追跡し、DisposeAsyncで自分のpointer Cancelと未解放キーのCancel→Upを、callerの取消tokenを使わずに配送します。FocusLostによる一括解除は使いません。物理入力の中立待ち・一時保留・UI実行箇所への配送は実hostの責務です。[サンプル補助](../../../samples/Lumyte.UI.Shared/README.md)は外部入力を転送しない専用モードの実装例であり、一般ホストのdispatcherではありません。

UIの通常入力へsnapshotを導入せず、コピーは自動操作からの明示要求時だけ行います。ScreenshotAsyncは新しい実描画frameのGPU完了・readbackを待つhostだけが実装し、CPU snapshotから作った図を返してはいけません。OS入力・DOM入力、アクセシビリティ、Diagnosticsの接続は別の境界です。

[設計](../../../docs/adr/ui/UI-0009-ui-automation.md)／[Native・headlessサンプル](../../../samples/Lumyte.UI.Sample/README.md)／[Browserサンプル](../../../samples/Lumyte.UI.Browser.Sample/README.md)

`CheckAsync` / `UncheckAsync` は Check capability を要求し、既に目的値なら入力しません。変更時は通常のactionability/probeと入力leaseの下で `UiCheckInput` を一度配送し、三状態でも目的値を直接確定します。`SelectOptionAsync(key)` は group の SelectOption capability と option の Click capability を確認し、通常の Move/Down/Up を一系列だけ配送します。callback後の自動再送は行いません。

ScrollAsyncはlocatorの表示領域中央から通常UiWheelInputを送ります。差分は有限の論理pixelで、
排他的input lease、Popup routing、通常の入れ子残余を共有し、Widget setterを操作しません。

## 数値の閉じた操作

SetValueAsync/SetRangeAsyncは実部品の専用capabilityを要求する。通常の入力lease取得後に現在の幾何とprobeを再確認し、UiSetNumericInputを一回配送する。Rangeは二値を同時に受理し、callback後の再送はしない。NumericBoxは既存Fillを使用する。snapshot.Numericは実際の数値と妥当性、従来のValueは引き続き0〜1の正規化値である。
