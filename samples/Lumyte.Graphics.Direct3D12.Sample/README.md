# Direct3D 12 バッファ転送サンプル

`IGraphicDevice` で Upload → Default → Readback を実行し、4093 byte の内容を照合します。
copy の開始位置はそれぞれ異なる非ゼロ offset を使用します。GUI は表示しません。
Agility SDK は一般版 `1.620.1`（SDK 620）が既定です。ビルド引数 `--agility-channel Preview` で
`1.722.0-preview`（SDK 722）へ切り替えられます。Preview の実行には Windows の開発者モードが必要です。
結果には実際の device の SDK version を表示します。

```powershell
dotnet run --project build -c Release -- --target Compile --project samples/Lumyte.Graphics.Direct3D12.Sample --agility-channel Stable
dotnet run --project samples/Lumyte.Graphics.Direct3D12.Sample -c Release --no-build -- warp
dotnet run --project samples/Lumyte.Graphics.Direct3D12.Sample -c Release --no-build -- hardware
```

プレビュー版へ切り替える場合は、上のビルドコマンドの `Stable` を `Preview` に変更して再ビルドします。
`--no-build` での実行は最後にビルドした版を使用します。

Windows、MSVC、Windows SDK が必要です。[バックエンドの対応範囲](../../src/Graphics/Lumyte.Graphics.Direct3D12/README.md)も参照してください。
