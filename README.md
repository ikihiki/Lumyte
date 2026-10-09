# Lumyte

C# を中心に、DirectX／Vulkan の Native バックエンドに C++ を使用するゲームエンジン。

- [開発環境のセットアップ](docs/development-environment.md)
- [ADR の書き方と運用](docs/adr/0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](docs/adr/0002-repository-layout.md)
- [mise による共通開発環境の設計](docs/adr/0003-development-environment.md)
- [Input システムの設計](docs/adr/input/INPUT-0001-input-system.md)
- [コードスタイルと lint](docs/development-environment.md#コードスタイル)

- [PSOのシェーダープログラムと描画状態の分離](docs/adr/graphics/GRAPHICS-0008-pipeline-programs-and-render-state.md)
- [シェーダーの設計](docs/adr/graphics/GRAPHICS-0006-shader-compilation-and-modules.md)／[Slang コンパイラー](src/Graphics/Lumyte.Graphics.Shaders/README.md)
- [Argument Tableと型付きGPU参照](docs/adr/graphics/GRAPHICS-0005-argument-tables-and-gpu-references.md)
- [サンプラーの設計](docs/adr/graphics/GRAPHICS-0004-samplers.md)
- [テクスチャとViewの設計](docs/adr/graphics/GRAPHICS-0003-textures-and-views.md)
- [型付きbufferの設計](docs/adr/graphics/GRAPHICS-0002-typed-buffers.md)
- [グラフィックデバイスの設計](docs/adr/graphics/GRAPHICS-0001-graphics-device.md)
- [Graphics.Abstractions](src/Graphics/Lumyte.Graphics.Abstractions/README.md)
- [Wgpu](src/Graphics/Lumyte.Graphics.Wgpu/README.md)／[Browser](src/Graphics/Lumyte.Graphics.Browser/README.md)／[Vulkan](src/Graphics/Lumyte.Graphics.Vulkan/README.md)
- [DeviceCaps サンプル](samples/Lumyte.Graphics.DeviceCaps.Sample/README.md)／[Browser サンプル](samples/Lumyte.Graphics.Browser.Sample/README.md)
- [Composition の設計](docs/adr/composition/COMPOSITION-0001-declarative-composition.md)
- [Composition の利用例](samples/Lumyte.Composition.Sample/README.md)
- [アニメーションシステムの設計](docs/adr/animation/ANIMATION-0001-animation-system.md)
- [汎用状態機械の設計](docs/adr/core/CORE-0001-state-machine.md)／[利用例](samples/Lumyte.StateMachines.Sample/README.md)
- [状態機械によるアニメーションの設計](docs/adr/animation/ANIMATION-0002-animation-state-machine.md)／[利用例](samples/Lumyte.Animation.StateMachine.Sample/README.md)
- [アニメーションの利用例](samples/Lumyte.Animation.Sample/README.md)

ソリューションは `Lumyte.slnx` を使用します。`mise exec -- dotnet test Lumyte.slnx -c Release`
で `tests/` 内のソリューションに登録されたテストを実行できます。
CI は Linux／Windows の x64／aarch64 で、mise セットアップ → 環境検証 → ビルド → テストの順に実行します。
すべての push と PR を対象に、TRX 形式の結果を構成ごとに保存します。
