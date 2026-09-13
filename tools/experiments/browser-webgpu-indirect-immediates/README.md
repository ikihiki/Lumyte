# Browser の indirect dispatch と immediate data

確認日: 2026-09-13。Portable Browser backend の実装試験で見つかった Dawn 側の不具合を、C#、Lumyte、shader compiler を使わずに再現する独立実験。

Edge **153.0.4234.32** では、`immediateSize: 4`、`var<immediate> value: u32`、`setImmediates(0, new Uint32Array([37]))` と `dispatchWorkgroupsIndirect` を組み合わせると、期待値 `[37, 38]` に対して **`[65535, 65536]`** が返った。GPU validation error は発生しない。同じ入力の直接 `dispatchWorkgroups(2)` は `[37, 38]` を返す。

65535 は、この device の `maxComputeWorkgroupsPerDimension` と一致する。Dawn の indirect 引数検証が内部 immediate data を書き換えた後、利用者の値を復元しない問題と整合する。[2026-09-07 の Dawn 修正](https://dawn.googlesource.com/dawn/+/c4e47b5eddc06f271cb07c3108cfccb1bb4704ec) は、[元の変更](https://dawn.googlesource.com/dawn/+/fc5539cb) を取り消している。browser の製品 version だけから修正の取り込みを推測せず、この GPU readback と C# conformance の双方を確認する。

Chrome for Testing **155.0.8048.0** では、C# backend を使う元の indirect dispatch 試験が期待値 `[37, 38]` のまま成功した。20件の Browser conformance と21件の timeline unit tests がすべて成功し、runtime に応じた skip や別の期待値は設けていない。

保存した standalone runner でも同日、以下を再確認した。双方の adapter 表示は NVIDIA／Turing、`maxImmediateSize = 64`、`maxComputeWorkgroupsPerDimension = 65535`。どちらも shader compilation messages、三種類の error scope、uncaptured errors、意図しない device loss はなかった。

| Browser | 直接 dispatch | 間接 dispatch | runner 終了値 |
| --- | --- | --- | --- |
| Edge 153.0.4234.32 | `[37, 38]` | `[65535, 65536]` | 1（期待した不具合の再現） |
| Chrome for Testing 155.0.8048.0 | `[37, 38]` | `[37, 38]` | 0 |

今回の結果は `artifacts/experiments/browser-webgpu-indirect-immediates/` の `c879a2d1-a179-4757-9ddb-44ae13e1cf26/result.json`（Edge）と `0e1e3103-d93d-4679-ac81-7fd6ea17f072/result.json`（Chrome）に保存した。これはこのPCの実測であり、すべてのGPU・browser版に対する保証ではない。

## 再現

Node.js 24 と WebGPU immediate 対応の Chromium browser を使う。repository root から実行する。

```powershell
node tools/experiments/browser-webgpu-indirect-immediates/run-probe.mjs
```

既定は Windows の Edge。別 browser は第1引数または `LUMYTE_WEBGPU_BROWSER` で指定できる。

```powershell
node tools/experiments/browser-webgpu-indirect-immediates/run-probe.mjs 'E:/path/to/chrome.exe'
```

runner は loopback の動的 port と新しい専用 profile を使い、headless browser を起動する。ユーザーの profile や既存 browser に接続しない。実験後は process と HTTP server を停止する。unsafe／experimental flag、validation の無効化、root の buffer 置換はない。

`artifacts/experiments/browser-webgpu-indirect-immediates/<GUID>/result.json` に browser version、adapter、limits、compilation info、error scope と readback を保存する。直接／間接の両方が期待値と一致した場合だけ終了コード0となる。`probe.js` の関数は WebGPU device を持つページでも単独で実行できる。

この実験は upstream の切り分け用であり、production backend を使う [xUnit conformance](../../../src/graphics/Lumyte.Graphics.WebGPU.Browser.Tests/Integration/README.md) の代用にはしない。既知の失敗を通すために conformance の期待値を変えない。
