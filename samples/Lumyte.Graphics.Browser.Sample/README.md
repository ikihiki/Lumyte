# Browser DeviceCaps サンプル

.NET WebAssembly から BrowserDevice を生成し、[共有プロジェクト](../Lumyte.Graphics.Shared/README.md)の `CapsDisplay` で Caps を表示します。ブラウザーの WebGPU device は表示後に解放します。

## 実行

[共通開発環境](../../docs/development-environment.md)をセットアップし、リポジトリルートから実行します。

```sh
source tools/setup/activate.sh
mise run setup-wasm
dotnet publish samples/Lumyte.Graphics.Browser.Sample -c Release
python3 -m http.server 8000 --directory samples/Lumyte.Graphics.Browser.Sample/bin/Release/net10.0/publish/wwwroot
```

WebGPU に対応するブラウザーで `http://localhost:8000` を開きます。正常なら Caps が表示され、失敗なら例外の内容が表示されます。JavaScript module の配置もこのプロジェクトが行います。

Node.js 24 と Chromium がある環境では、mise タスクで導入・publish・headless browser での検証をまとめて実行できます。

```sh
mise run test-wasm
```

publish 済みのサンプルを検証する場合は、次のコマンドを使います。

```sh
node tools/graphics/verify-browser.mjs
# Chromium の実行ファイル名が異なる場合
CHROME=/usr/bin/google-chrome node tools/graphics/verify-browser.mjs
```

この検証は actual WebGPU device を作り、.NET の共通 API からの表示結果を確認します。GPU がない環境では Chromium の SwiftShader を使用します。ブラウザーを生成できない場合は失敗します。

このサンプルは `wasm-tools` を必要とするため `Lumyte.slnx` に登録していません。Browser library 自体は通常の .NET SDK でソリューションからビルドできます。既存 CI の Linux x64 ジョブは `mise run test-wasm` で、固定 workload の導入・publish・実 WebGPU と WebAssembly による検証を実行します。
