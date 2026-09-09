# Slang / WGSL direct root 実験

[調査結果と採用方針](../../../docs/designs/slang-wgsl-root-investigation.md) を再現するための独立した手動 GPU 実験。production project と通常の xUnit テストには組み込まない。compiler fixture は失敗する対照例も含む。

## 必要なもの

- Windows、PowerShell 7、Node.js 24。
- Slang 2026.17 の [公式 Windows x86_64 archive](https://github.com/shader-slang/slang/releases/tag/v2026.17)。ZIP の SHA-256 は `bc8cf08b24aaf44d98f06b7d578d0557a21bfed1ce6bfb9765a0f0d21dec8f31`。compiler と同梱 library を一緒に展開する。
- `immediate_address_space` と `setImmediates` に対応する Chromium 系 browser。確認済みは Edge 152.0.4191.66。

## 再現

repository root で実行する。`$probeCompiler` は展開した `slangc.exe` のパスへ変更できる。以下の値はこの調査で使った配置であり、compiler binary 自体は repository に含めない。

```powershell
$probeCompiler = Join-Path $PWD 'artifacts/experiments/slang-wgsl-root/compiler/slang-2026.17/bin/slangc.exe'
$probeOutput = Join-Path $PWD ('artifacts/experiments/slang-wgsl-root/reproduce-' + [Guid]::NewGuid().ToString('N'))
& ./tools/experiments/slang-wgsl-root/compiler/run-probe.ps1 -CompilerPath $probeCompiler -OutputDirectory $probeOutput
$env:LUMYTE_SLANG_PROBE_OUTPUT = (Get-ChildItem -LiteralPath $probeOutput -Directory).FullName
node ./tools/experiments/slang-wgsl-root/runtime/browser-probe.mjs reproduction
Remove-Item Env:/LUMYTE_SLANG_PROBE_OUTPUT
```

browser の場所は `LUMYTE_WEBGPU_BROWSER` 環境変数、または run 名の次の引数で指定できる。既定は Windows の Edge。実験専用 profile を `artifacts/experiments/slang-wgsl-root/runtime/` に作り、ユーザーの通常の profile は使用しない。

既存の Slang 2026.7.1 と比較する場合は compiler script の `-CompilerPath` に `.packages/slang/2026.7.1/bin/slangc.exe` を指定する。runtime manifest の hash は 2026.17 の生成物に固定しているため、別版の GPU 実験では別の manifest として生成物・版を明示的に管理する。

## 読み方

compiler script は各実行で新しい `run-<GUID>` を作り、13 fixture × 3 target の引数、終了値、diagnostics、reflection、生成物と SHA-256 を `results.json` に記録する。非ゼロ終了の生成物を成功例に使わない。script 自体の完了は WGSL の合法性や全 fixture の成功を意味しない。`HasImmediate` 等は生成 text の観測値であり、独自の shader validator ではない。

runtime は `target_switch_immediate` と `scalar_getter_mixed_immediate` の生成物を hash 確認してから、変更せず browser に渡す。手書き WGSL の対照例も実行し、shader diagnostics、error scope、device loss、queue 完了と readback を記録する。異なる root 値、padding、記録後の CPU 配列変更、手書き vertex／fragment の参照を確認する。root 用 GPU buffer は作らない。

`artifacts/experiments/slang-wgsl-root/runtime/reproduction.json` の `result.result.value.passed` が true で、Node が終了コード 0 を返せばその実行は成功。browser、adapter、compiler 版と hash を同じ JSON に保存する。対応機能なし、shader hash 不一致、GPU 出力不一致や runtime error は成功にしない。

`target_switch` は Native と Portable の compiler 出力を比較する実験上の入力であり、両系統の GPU ABI を統一する公開 API の提案ではない。`typed_mixed_immediate` と `intrinsic_immediate` の別 target 出力は、compiler が text を出せても正しい実行物とは限らないことを示す対照例として保持する。
