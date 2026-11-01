# Lumyte.UI.Browser.Sample

BrowserのcanvasとHostedの領域を同じ入力取得列へ接続するサンプルです。青いButtonはNative提示root、橙色のButtonはHostedです。pointerで対象を選び、Tab／Shift-Tab、Space／Enterでも操作できます。クリック数はconsoleへ出力します。

.NET 10 SDK、WebAssembly対応環境、Slang、WebGPU対応Browserを使用します。

```sh
dotnet publish samples/Lumyte.UI.Browser.Sample -c Release
# bin/Release/net10.0/publish/wwwroot をlocalhostのHTTPサーバーで配信する
```

`wwwroot/index.html`を開きます。Canvas外で解放した場合はClickせず、外から戻って解放すると一度だけClickします。Browserでは独立したNative子Windowを作らず、WindowSystemのHostedを使用します。

描画はRGBA16Floatへ記録した後、UNORM swapchainへ提示します。DPIはCanvasの行列で一度だけ適用し、Drawing2DのPixelScaleは1です。提示のGPU完了を毎frame待つ検証用サンプルで、frameの並列実行やGPU画像の再利用は行いません。Stopは実行中frameの完了後に入力・GPU・UI・Windowを解放します。

## 自動確認

Chromiumを使うCDP smoke testを同梱しています。追加のNode packageは不要です。

```sh
node samples/Lumyte.UI.Browser.Sample/verify-browser.mjs
# GPUを使用せず、実DOM入力とCPUの描画記録だけを確認する場合
LUMYTE_UI_INPUT_ONLY=1 node samples/Lumyte.UI.Browser.Sample/verify-browser.mjs
```

`CHROME`で実行ファイル、`LUMYTE_SCREENSHOT`で画像保存先を指定できます。DOM capture、canvas外Up、Hosted、keyboard、DPI×2、終了を確認します。`?input-only`はテスト用モードです。描画結果は画面へ提示しません。

2026-10-29の環境では、publishと入力・CPU記録の実Browser操作が成功しました。GPU提示は`mapAsync`の`A valid external Instance reference no longer exists`で失敗し、Lumyteを使わない最小JavaScriptでも同じエラーを再現しました。この環境でGPU提示が成功したとは扱いません。Native AOTは未検証です。

## RenderGraphとResourcesの選択

通常URLは直接描画、`?render-graph`は親graph、`?resources`はDIで設定したDrawing2D資源と親graphを使用します。両方のsurfaceを同じCanvasへ記録し、targetを一度だけimportしてUI passを追加します。親graphがSampled状態へexportしたtargetを既存present passで表示します。StopはGPU完了後にframe資源を返し、ReferenceとDI所有者をdeviceより先に非同期破棄します。

```sh
LUMYTE_UI_RENDER_GRAPH=1 node samples/Lumyte.UI.Browser.Sample/verify-browser.mjs
LUMYTE_UI_RESOURCES=1 node samples/Lumyte.UI.Browser.Sample/verify-browser.mjs
```

`?input-only`との併用時はGPUとResourcesを初期化せず、従来のCPU記録確認を行います。自動device復旧はサンプルに含めません。

2026-10-29に新しい2モードもpublishして実行しました。`?resources`はBrowserのcooperative実行を使います。両モードのGPU提示は上記と同じmapAsyncエラーで停止し、描画成功とは扱いません。入力・CPU記録のsmoke testは成功しました。

## Locatorによる自動操作

`?automation`でC#のUiAutomationPageから、保存ButtonのClick、成功indicatorのExpect、Enterによる保存、focusのExpectを実行します。スクリプト実行中は通常のDOM入力をsurfaceへ配送せず、入力leaseを使います。完了後は通常のframeと入力へ戻ります。GPUなしの`?input-only&automation`でも同じ操作を検証できます。

```sh
LUMYTE_UI_AUTOMATION=1 LUMYTE_UI_INPUT_ONLY=1 node samples/Lumyte.UI.Browser.Sample/verify-browser.mjs
LUMYTE_UI_AUTOMATION=1 LUMYTE_AUTOMATION_SCREENSHOT=/tmp/ui-automation.png node samples/Lumyte.UI.Browser.Sample/verify-browser.mjs
```

GPUモードのPNGはC#のScreenshotAsyncが新しく描画・readbackした対象surfaceの実画素です。JSホストへbase64で渡し、上記変数で保存できます。CDPの画面キャプチャとは別です。サンプルはcold pipeline準備を含め30秒の期限を指定します。

2026-10-29にpublishし、実ChromiumでC#の保存操作・Expect・Enter、その後のDOM capture、領域外Up、Hosted、keyboard、DPI×2、終了が成功しました。GPU提示・PNGはこの環境で既知のmapAsyncエラーにより未検証です。Native AOTは未検証です。

## Diagnostics経由の操作

`?input-only&diagnostics`で、既存HTTP Agentからこのsurfaceを診断サービスへ公開できます。GPUを使う`?diagnostics`では実readbackのScreenshotも公開します。専用モードの間はDOM入力をsurfaceへ転送せず、Operatorが通常UI入力を操作します。Rootの終了時にはJobとhost清掃を待ちます。

bootstrapの`globalThis.lumyteDiagnosticService`へサービスURL、`globalThis.lumyteDiagnosticGameToken`へゲーム用tokenをホストが一時供給します。tokenをqueryや生成artifactへ含めません。サービスにはBrowserのoriginを明示してCORSを設定します。OperatorTokenは別プロセスのOperatorだけが持ちます。

publishと、診断サービス・型付きOperatorのRelease build後に実行します。

```sh
python3 tools/diagnostics/ui-browser-smoke.py
```

2026-10-29に実ChromiumのHTTP Agentと別プロセスOperatorで、対象発見、control、Click、indicatorのExpect、Enter、count、解放と終了が成功しました。この経路はDOM入力そのものの検証とは分けて扱います。Browser publish時のtrim解析も確認し、既存DI登録のPublicConstructors注釈を修正しました。wasm-toolsによるnative最適化とNative AOTは未検証です。GPU提示／PNGは既知のmapAsync環境エラーにより未検証です。
