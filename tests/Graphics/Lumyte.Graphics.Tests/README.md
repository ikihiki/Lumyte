# Graphics device テスト

実際に生成した Wgpu／Vulkan device を `IGraphicDevice` として受け取り、各上限・alignment の妥当性、Caps の instance の安定性、繰り返し読み取りでの allocation がないこと、record のコピーが元の snapshot を変更しないことを検証します。起動と終了だけがバックエンドの具象型に依存します。

GPU または software driver がある環境で明示的に有効にします。

```sh
LUMYTE_GRAPHICS_GPU_TESTS=1 dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release
```

環境変数を指定しない場合は GPU テストを skip します。有効にした場合、device を生成できないことを成功として扱いません。Browser の実行は [.NET WebAssembly サンプル](../../../samples/Lumyte.Graphics.Browser.Sample/README.md) で確認します。

[既存 CI](../../../.github/workflows/composition.yml) の Linux x64 ジョブで、セットアップ済みの lavapipe を使って native device の GPU テストを有効にします。同じジョブで `mise run test-wasm` による browser WebGPU の検証も実行します。

BufferTests は layout の要素単位 alignment と overflow を検証し、GPU テストでは共通の BufferExercise を Wgpu／Vulkan device に適用します。Browser CI のサンプルも同じ処理を実行します。buffer に GPU コピー命令や自動 staging がないことを前提に、CPU access の方向・mapping・範囲と寿命を検証します。

TextureTestsはshared projectのTextureExerciseをWgpu／Vulkanで実行します。format、mip／array／cube範囲、正規化、copy専用textureのView拒否、利用完了後の明示的な破棄を共通APIだけで検証します。同じexerciseを既存CIのBrowser sampleでも実行します。

SamplerTestsはshared projectのSamplerExerciseをWgpu／Vulkanで実行します。default・LOD 0固定・全filter／address／comparison・anisotropy制約と寿命を共通APIで検証します。同じexerciseを既存Wasm／Browser CIでも実行します。

ArgumentTableTestsはshared projectのArgumentTableExerciseをWgpu／Vulkanで実行し、論理slot・型付き要素・登録の置き換えと別deviceの登録拒否を共通APIだけで検証します。同じexerciseを既存Wasm／Browser CIでも実行します。

`ShaderTests` は wgpu と Vulkan で DLL のオフライン成果物、Slang のオンライン成果物、module作成、offline binaryの全target収録とmetadata、online binaryの未収録target拒否を検証します。共通 API を使う検証本体を Shared に置き、具体的な device／compiler の生成だけをテスト bootstrap に残します。GPU tests は既存 CI の Linux x64 ジョブ、Browser のオフライン module 検証は既存 Wasm ジョブで実行します。

オンラインコンパイルはtargetの省略／nullで全対応targetを生成する経路と、各targetのみを指定する経路を既存CIの全構成で検証します。

AdvancedCommandTestsはIndirect命令のwire layoutと、Wgpu／Vulkan（pipeline cache有効・無効）のdepth/stencil・indexed/indirect GPU検証を行います。同じ共有ライブラリのexerciseをBrowser sampleから実行し、既存CIのBrowser検証でも成功reportを必須にします。

Surfaceのnativeハンドル受け取り口は、無効なsourceをネイティブ生成前に拒否するテストで確認します。
Browser CIは外部OffscreenCanvas contextと共通SurfaceExerciseを使って取得、clear／readback、提示、明示的な完了待機、再構成を検証します。
Wgpu／Vulkanの実ウインドウでのWSI実行は、ハンドル取得を提供する後続PRで検証します。

SemaphoreTestsはWgpu／Vulkanで共通SemaphoreExerciseを実行し、CPU完了を待たずに発行するbinary semaphoreチェーンと、完了後の明示的な再利用を確認します。
Browser CIも同じexerciseを実行し、SurfaceExerciseでは取得・Submit・Presentのsemaphoreを明示します。

Deviceの生成設定はnative instance生成前に検証します。生成済みDeviceのSurface入口では空／null／無効な後続source／重複を拒否し、提示用拡張を有効化していないVulkan Deviceではcallbackを呼ばないことを確認します。
Browser CIは二つの外部contextで共通MultiSurfaceExerciseを実行し、同時取得、全画素readback、一括／別々のSubmit、他方取得中のresize／closeと継続描画を必須reportとして検証します。

resourceの寿命・登録参照の有効性・shader ABI・texture状態・wait／signalの整合性は利用者側の契約です。draw／Submit時にこれらの不正使用をAPIが検出することはテストせず、明示的な同期・転送とGPU結果の正しさを既存CIで検証します。
