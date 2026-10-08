# Browser DeviceCaps サンプル

.NET WebAssembly から BrowserDevice を生成し、[共有プロジェクト](../Lumyte.Graphics.Shared/README.md)の `CapsDisplay` で Caps を表示します。ブラウザーの WebGPU device は表示後に解放します。

## 実行

.NET 10 と `wasm-tools` workload を用意して、リポジトリルートから実行します。

```sh
dotnet workload install wasm-tools
dotnet publish samples/Lumyte.Graphics.Browser.Sample -c Release
python3 -m http.server 8000 --directory samples/Lumyte.Graphics.Browser.Sample/bin/Release/net10.0/publish/wwwroot
```

WebGPU に対応するブラウザーで `http://localhost:8000` を開きます。正常なら Caps が表示され、失敗なら例外の内容が表示されます。JavaScript module の配置もこのプロジェクトが行います。

Node.js 24 と Chromium がある環境では、publish 後に headless browser で実行を検証できます。

```sh
node tools/graphics/verify-browser.mjs
# Chromium の実行ファイル名が異なる場合
CHROME=/usr/bin/google-chrome node tools/graphics/verify-browser.mjs
```

この検証は actual WebGPU device を作り、.NET の共通 API からの表示結果を確認します。GPU がない環境では Chromium の SwiftShader を使用します。ブラウザーを生成できない場合は失敗します。

このサンプルは `wasm-tools` を必要とするため `Lumyte.slnx` に登録していません。Browser library 自体は通常の .NET SDK でソリューションからビルドできます。ブラウザー側のテストには実際の WebGPU と WebAssembly を使用してください。
