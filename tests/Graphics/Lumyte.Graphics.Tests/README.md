# Graphics device テスト

実際に生成した Wgpu／Vulkan device を `IGraphicDevice` として受け取り、各上限・alignment の妥当性、Caps の instance の安定性、繰り返し読み取りでの allocation がないこと、record のコピーが元の snapshot を変更しないことを検証します。起動と終了だけがバックエンドの具象型に依存します。

GPU または software driver がある環境で明示的に有効にします。

```sh
LUMYTE_GRAPHICS_GPU_TESTS=1 dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release
```

環境変数を指定しない場合は GPU テストを skip します。有効にした場合、device を生成できないことを成功として扱いません。Browser の実行は [.NET WebAssembly サンプル](../../../samples/Lumyte.Graphics.Browser.Sample/README.md) で確認します。

[既存 CI](../../../.github/workflows/composition.yml) の Linux x64 ジョブで、セットアップ済みの lavapipe を使って native device の GPU テストを有効にします。同じジョブで `mise run test-wasm` による browser WebGPU の検証も実行します。
