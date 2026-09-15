# Browser WebGPU conformance

隣接 xUnit project の `WebGpuBrowserConformance` category は、実際の C# backend を .NET WebAssembly interpreter で実行する。JavaScript だけの GPU probe を実装の代替にしない。通常の timeline unit tests は browser を使わない。

## 実行

repository root で実行する。2026-09-13 の確認では **Chrome for Testing 155.0.8048.0** で24件の Browser conformance と21件の timeline unit tests が成功した。下記の path は今回の配置例であり、browser binary は Git に含めない。

```powershell
$env:LUMYTE_WEBGPU_BROWSER = Join-Path $PWD 'artifacts/tools/chrome-for-testing/155.0.8048.0/chrome-win64/chrome.exe'
dotnet test src/graphics/Lumyte.Graphics.WebGPU.Browser.Tests/Lumyte.Graphics.WebGPU.Browser.Tests.csproj
```

build は参照した `BrowserHost` を一度 publish し、test output の `BrowserHost/wwwroot/` に配置する。`--no-build` を使う場合は先に通常の build が必要。SDK の解釈実行を使い、AOT、native relinking、trimming は今回の conformance 対象にしない。追加の global workload や npm package を要求しない。

既定の browser は Windows の Microsoft Edge。別の対応 Chromium を使う場合は `LUMYTE_WEBGPU_BROWSER` に実行ファイルの絶対パスを指定する。browser 不在、直接 root 非対応、host 起動失敗は明示的に失敗し、skip や成功に置き換えない。

Edge 153.0.4234.32 では、直接 root と indirect dispatch の組合せに Dawn の既知不具合を再現した。root の37が内部検証用の65535に置換され、validation error なしで誤った結果になる。直接 dispatch は正しい。確認した Chrome for Testing 155.0.8048.0 は、同じ C# indirect dispatch の期待値 `[37, 38]` を満たした。修正済み runtime を `LUMYTE_WEBGPU_BROWSER` で選択して試験する。該当試験の期待値を変更せず、feature 検証や browser の安全機能も無効化しない。[独立した再現実験](../../../../tools/experiments/browser-webgpu-indirect-immediates/README.md) と [Dawn の修正](https://dawn.googlesource.com/dawn/+/c4e47b5eddc06f271cb07c3108cfccb1bb4704ec) を参照する。

fixture は loopback の動的 port で publish 出力だけを配信し、headless browser と新しい専用 profile を使う。ユーザーの profile と開いている browser は使用しない。experimental／unsafe feature の起動指定はない。Browser 専用の named mutex を使い、同じ Browser backend の適合試験だけを直列化する。Dawn／DirectX 12／Vulkan と GPU を使わない単体テストは並行できる。browser process と HTTP server は fixture 終了時に停止する。

ソリューション全体は `dotnet test Lumyte.slnx` の project 並列実行を使う。以前の全 backend 共通 mutex による排他待ちを、backend 別の mutex へ分けた。`--blame-hang-timeout 2m` の監視条件は維持できる。同じ backend の試験を別コマンドでも同時起動すると、その排他待ちは引き続き監視対象になるため、通常は一つの全体実行にまとめる。[テストの実行と並列化](../../../../docs/testing.md)を参照する。

`artifacts/tests/webgpu-browser/<GUID>/` に browser version、ケース別の JSON 結果、browser log を保存する。profile も同じ隔離 directory に置く。

## 検証する動作

- .NET の browser 実行、直接 root と device の有効 limit。
- 初回 module import 失敗後に正しい URL から再試行できること。
- JS の整数精度を超える Buffer size の拒否と、同期 WebIDL encode 失敗後の未発行 timeline 値の再利用。
- 8 byte root の snapshot と pipeline 再設定、32 byte の混在型と padding。
- Portable shader package／loader が所有する module・layout からの compute 実行と直接 root、package 経由でも保持される runtime の shader 診断。
- GPU が生成した間接 dispatch 引数、dynamic uniform offset の snapshot。
- indexed raster の index range、firstIndex、signed baseVertex と直接 root。
- 同じコンパイル済み 2D consumer を browser WASM で実行し、図形・画像の path clip・HDR layer を desktop 側の SkiaSharp 比較画像と照合すること。SkiaSharp の描画は browser 内では実行しない。
- immutable Texture／Sampler binding による texture sampling。
- Buffer／Texture と Texture 間 copy、複数行の明示 row pitch。
- Portable Buffer／Texture pool への完了後の返却と再貸出。同じ handle と書込み済みの byte／pixel が保持されること。
- offset 付き map write の反映、未編集 byte の保持、失敗した二回目の map による既存 lease の保持、read mapping と unmap 後の memory lease。
- runtime の resource、shader、pipeline、command 診断と batch 単位の失敗。
- 正確な ulong CPU timeline、受理していない値の拒否、記録の再提出拒否、待機取消しの独立性。

browser の GPU device loss／out-of-memory を意図的に発生させる試験、別 browser／OS／adapter、AOT／trimming、canvas presentation は未検証。timeline の制御可能な失敗順序は fake task を使う通常 xUnit tests に分ける。
