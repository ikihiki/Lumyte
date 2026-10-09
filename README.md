# Lumyte

C# を中心に、DirectX／Vulkan の Native バックエンドに C++ を使用するゲームエンジン。

- [開発環境のセットアップ](docs/development-environment.md)
- [ADR の書き方と運用](docs/adr/0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](docs/adr/0002-repository-layout.md)
- [mise による共通開発環境の設計](docs/adr/0003-development-environment.md)
- [DI で通信方式を選択するゲームエンジン診断システム](docs/adr/diagnostics/DIAGNOSTICS-0001-diagnostics-transport.md)
- [診断基盤の API と利用方法](src/Diagnostics/Lumyte.Diagnostics/README.md)
- [診断 Input サンプル](samples/Lumyte.Diagnostics.Sample/README.md)
- [診断の性能・シリアライズ比較](docs/benchmarks/diagnostics/README.md)
- [Input システムの設計](docs/adr/input/INPUT-0001-input-system.md)
- [コードスタイルと lint](docs/development-environment.md#コードスタイル)

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
- [アニメーションの利用例](samples/Lumyte.Animation.Sample/README.md)

ソリューションは `Lumyte.slnx` を使用します。`mise exec -- dotnet test Lumyte.slnx -c Release`
で `tests/` 内のソリューションに登録されたテストを実行できます。
CI は Linux／Windows の x64／aarch64 で、mise セットアップ → 環境検証 → ビルド → テストの順に実行します。
すべての push と PR を対象に、TRX 形式の結果を構成ごとに保存します。
