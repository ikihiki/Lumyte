# Graphics device テスト

実際に生成した Wgpu／Vulkan device を `IGraphicDevice` として受け取り、各上限・alignment の妥当性、Caps の instance の安定性、繰り返し読み取りでの allocation がないこと、record のコピーが元の snapshot を変更しないことを検証します。起動と終了だけがバックエンドの具象型に依存します。

GPU または software driver がある環境で明示的に有効にします。

```sh
LUMYTE_GRAPHICS_GPU_TESTS=1 dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release
```

環境変数を指定しない場合は GPU テストを skip します。有効にした場合、device を生成できないことを成功として扱いません。Browser の実行は [.NET WebAssembly サンプル](../../../samples/Lumyte.Graphics.Browser.Sample/README.md) で確認します。

[Graphics device capabilities CI](../../../.github/workflows/graphics-caps.yml) は、graphics の実装・サンプル・テストと共通ビルド設定が変わった場合に software Vulkan と browser WebGPU の検証を行います。Markdown のみの変更ではこの workflow を起動しません。
