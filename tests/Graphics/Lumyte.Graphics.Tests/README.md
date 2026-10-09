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

ArgumentTableTestsはshared projectのArgumentTableExerciseをWgpu／Vulkanで実行し、論理slot・型付き要素・参照失効・resource leaseと別deviceの登録拒否を共通APIだけで検証します。同じexerciseを既存Wasm／Browser CIでも実行します。

`ShaderTests` は wgpu と Vulkan で DLL のオフライン成果物、Slang のオンライン成果物、module lifetime、offline binaryの全target収録とmetadata、online binaryの未収録target拒否を検証します。共通 API を使う検証本体を Shared に置き、具体的な device／compiler の生成だけをテスト bootstrap に残します。GPU tests は既存 CI の Linux x64 ジョブ、Browser のオフライン module 検証は既存 Wasm ジョブで実行します。

オンラインコンパイルはtargetの省略／nullで全対応targetを生成する経路と、各targetのみを指定する経路を既存CIの全構成で検証します。
