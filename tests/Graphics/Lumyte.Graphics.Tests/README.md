# Graphics device テスト

実際に生成した Wgpu／Vulkan device を `IGraphicDevice` として受け取り、各上限・alignment の妥当性、Caps の instance の安定性、繰り返し読み取りでの allocation がないこと、record のコピーが元の snapshot を変更しないことを検証します。起動と終了だけがバックエンドの具象型に依存します。

GPU または software driver がある環境で明示的に有効にします。

```sh
LUMYTE_GRAPHICS_GPU_TESTS=1 dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release
```

環境変数を指定しない場合は GPU テストを skip します。有効にした場合、device を生成できないことを成功として扱いません。Browser の実行は [.NET WebAssembly サンプル](../../../samples/Lumyte.Graphics.Browser.Sample/README.md) で確認します。

[既存 CI](../../../.github/workflows/composition.yml) の Linux x64 ジョブで、セットアップ済みの lavapipe を使って native device の GPU テストを有効にします。同じジョブで `mise run test-wasm` による browser WebGPU の検証も実行します。

BufferTests は layout の要素単位 alignment と overflow を検証し、GPU テストでは共通の BufferExercise を Wgpu／Vulkan device に適用します。Browser CI のサンプルも同じ処理を実行します。buffer に GPU コピー命令や自動 staging がないことを前提に、CPU access の方向・mapping・範囲と寿命を検証します。

TextureTestsはshared projectのTextureExerciseをWgpu／Vulkanで実行します。format、mip／array／cube範囲、正規化、copy専用textureのView拒否、子resourceを保持したDispose拒否を共通APIだけで検証します。同じexerciseを既存CIのBrowser sampleでも実行します。

SamplerTestsはshared projectのSamplerExerciseをWgpu／Vulkanで実行します。default・LOD 0固定・全filter／address／comparison・anisotropy制約と寿命を共通APIで検証します。同じexerciseを既存Wasm／Browser CIでも実行します。
