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

2026-10-29の環境では、publishと入力・CPU記録の実Browser操作が成功しました。GPU提示は`mapAsync`の`A valid external Instance reference no longer exists`で失敗し、Lumyteを使わない最小JavaScriptでも同じエラーを再現しました。この環境でGPU提示が成功したとは扱いません。trimming／Native AOTの検証も未実施です。
