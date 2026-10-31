# Lumyte

C# を中心に、DirectX／Vulkan の Native バックエンドに C++ を使用するゲームエンジン。
文字組みにはRustのParleyを使い、COLR v1とMSDF/MTSDFを共通の2D描画へ接続します。

ビルド・テスト・publish・packは、公式構成のFallout runnerから実行します。

```sh
dotnet run --project build -c Release -- --target Compile
dotnet run --project build -c Release -- --target Test
```

必要なcompiler・SDKの準備は開発環境のセットアップを参照してください。

- [開発環境のセットアップ](docs/development-environment.md)
- [共通ビルド手順](build/README.md)／[ビルド工程の設計](docs/adr/0004-build-orchestration.md)
- [ADR の書き方と運用](docs/adr/0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](docs/adr/0002-repository-layout.md)
- [GraphicsのDI・デバイスロスト復旧](docs/adr/graphics/GRAPHICS-0016-device-di-and-loss-recovery.md)／[Hosting API](src/Graphics/Lumyte.Graphics.Hosting/README.md)
- [mise による共通開発環境の設計](docs/adr/0003-development-environment.md)
- [ワールドとスタック](src/Core/Lumyte.Worlds/README.md)／[設計](docs/adr/core/CORE-0002-world-stack.md)
- [汎用数学計算](src/Core/Lumyte.Mathematics/README.md)
- [設定管理](src/Core/Lumyte.Settings/README.md)／[設定管理の設計](docs/adr/settings/SETTINGS-0001-user-settings-persistence.md)
- [DI で通信方式を選択するゲームエンジン診断システム](docs/adr/diagnostics/DIAGNOSTICS-0001-diagnostics-transport.md)
- [設定の診断公開](src/Diagnostics/Lumyte.Diagnostics.Settings/README.md)／[設定診断の設計](docs/adr/settings/SETTINGS-0002-diagnostic-operations.md)
- [アニメーションの診断公開](src/Diagnostics/Lumyte.Diagnostics.Animation/README.md)／[アニメーション診断の設計](docs/adr/diagnostics/DIAGNOSTICS-0003-animation-operations.md)
- [Windowの診断公開](src/Diagnostics/Lumyte.Diagnostics.Windowing/README.md)／[Window診断の設計](docs/adr/diagnostics/DIAGNOSTICS-0005-window-inspection.md)
- [診断基盤の API と利用方法](src/Diagnostics/Lumyte.Diagnostics/README.md)
- [診断 Input サンプル](samples/Lumyte.Diagnostics.Sample/README.md)
- [OpenAL オーディオ](src/Audio/Lumyte.Audio.OpenAL/README.md)／[Web Audio](src/Audio/Lumyte.Audio.Browser/README.md)／[共通 API](src/Audio/Lumyte.Audio.Abstractions/README.md)
- [Audioの型付き配送](src/Audio/Lumyte.Audio.Execution/README.md)／[OpenAL実行例](samples/Lumyte.Audio.Execution.Sample/README.md)／[WebAudio実行例](samples/Lumyte.Audio.Execution.Browser.Sample/README.md)
- [Audioリソースの実装](src/Audio/Lumyte.Resources.Audio/README.md)／[AudioのDI登録](src/Audio/Lumyte.Resources.Audio.DependencyInjection/README.md)
- [オーディオ機構の設計](docs/adr/audio/AUDIO-0001-audio-system.md)
- [Input システムの設計](docs/adr/input/INPUT-0001-input-system.md)
- [Input の取得契約の分離](docs/adr/input/INPUT-0005-input-abstractions.md)
- [Platform・IWindow の設計](docs/adr/platform/PLATFORM-0001-platform-and-window-contracts.md)／[Window契約の分離と任意の Windowing 管理](docs/adr/window/WINDOW-0001-window-system.md)
- [Windowing の利用方法](docs/adr/window/WINDOW-0001-window-system.md#利用例)／[Windows の単一 Window サンプル](samples/Lumyte.Platform.Windows.Sample/README.md)
- [Clipboard の設計](docs/adr/platform/PLATFORM-0002-clipboard.md)
- [Input 診断](src/Input/Lumyte.Input.Diagnostics/README.md)／[監視・Tape・マッピング編集の設計](docs/adr/input/INPUT-0004-diagnostics-and-overrides.md)
- [コードスタイルと lint](docs/development-environment.md#コードスタイル)

- [文字描画・MSDF/MTSDF](src/Graphics/Lumyte.Graphics.Text/README.md)／[Parleyによる文字組み](src/Graphics/Lumyte.Graphics.Text.Parley/README.md)／[文字描画の設計](docs/adr/graphics/GRAPHICS-0024-text-rendering.md)
- [2D・3D共通画像エフェクトの設計](docs/adr/graphics/GRAPHICS-0017-image-effects.md)／[利用方法](src/Graphics/Lumyte.Graphics.ImageEffects/README.md)
- 2D描画の設計
  - [基本図形・paint・描画資源](docs/adr/graphics/GRAPHICS-0018-2d-graphics-engine.md)
  - [線・パス](docs/adr/graphics/GRAPHICS-0019-2d-paths-and-strokes.md)
  - [画像・mesh](docs/adr/graphics/GRAPHICS-0020-2d-images-and-meshes.md)
  - [文字向け形式・COLRv1](docs/adr/graphics/GRAPHICS-0021-2d-glyph-representations.md)
  - [RenderGraphレイヤー・Effects接続](docs/adr/graphics/GRAPHICS-0022-2d-render-graph-layers.md)
- [Depth／Stencil attachment](docs/adr/graphics/GRAPHICS-0010-depth-stencil-attachments.md)
- [Indexed drawとIndirect実行](docs/adr/graphics/GRAPHICS-0011-indexed-and-indirect-commands.md)
- [カラーテクスチャ形式の拡充](docs/adr/graphics/GRAPHICS-0012-color-texture-formats.md)
- [Surface・Swapchain・Present](docs/adr/graphics/GRAPHICS-0013-surface-swapchain-presentation.md)
- [利用者が管理するリソース寿命と実行時検証](docs/adr/graphics/GRAPHICS-0014-caller-managed-resource-validation.md)
- [PSOのシェーダープログラムと描画状態の分離](docs/adr/graphics/GRAPHICS-0008-pipeline-programs-and-render-state.md)
- [Argument Tableのシェーダー引数への接続](docs/adr/graphics/GRAPHICS-0009-shader-argument-binding.md)
- [CommandBufferと明示的なGPU実行](docs/adr/graphics/GRAPHICS-0007-command-buffers-and-submission.md)
- [シェーダーの設計](docs/adr/graphics/GRAPHICS-0006-shader-compilation-and-modules.md)／[Slang コンパイラー](src/Graphics/Lumyte.Graphics.Shaders/README.md)
- [Argument Tableと型付きGPU参照](docs/adr/graphics/GRAPHICS-0005-argument-tables-and-gpu-references.md)
- [サンプラーの設計](docs/adr/graphics/GRAPHICS-0004-samplers.md)
- [テクスチャとViewの設計](docs/adr/graphics/GRAPHICS-0003-textures-and-views.md)
- [型付きbufferの設計](docs/adr/graphics/GRAPHICS-0002-typed-buffers.md)
- [グラフィックデバイスの設計](docs/adr/graphics/GRAPHICS-0001-graphics-device.md)
- [Graphics.Abstractions](src/Graphics/Lumyte.Graphics.Abstractions/README.md)
- [RenderGraph の利用方法](src/Graphics/Lumyte.Graphics.RenderGraph/README.md)／[設計](docs/adr/graphics/GRAPHICS-0015-render-graph.md)／[診断連携](src/Diagnostics/Lumyte.Diagnostics.RenderGraph/README.md)
- [2D描画の利用方法](src/Graphics/Lumyte.Graphics.Drawing2D/README.md)／[Resourcesからの生成](src/Graphics/Lumyte.Resources.Graphics.Drawing2D/README.md)／[RenderGraph連携](src/Graphics/Lumyte.Resources.Graphics.Drawing2D.RenderGraph/README.md)
- [Wgpu](src/Graphics/Lumyte.Graphics.Wgpu/README.md)／[Browser](src/Graphics/Lumyte.Graphics.Browser/README.md)／[Vulkan](src/Graphics/Lumyte.Graphics.Vulkan/README.md)
- [DeviceCaps サンプル](samples/Lumyte.Graphics.DeviceCaps.Sample/README.md)／[Browser サンプル](samples/Lumyte.Graphics.Browser.Sample/README.md)
- [Composition の設計](docs/adr/composition/COMPOSITION-0001-declarative-composition.md)
- [Composition の利用例](samples/Lumyte.Composition.Sample/README.md)
- [UIツリー・レイアウト・描画・ポインター入力の利用方法](src/UI/Lumyte.UI/README.md)／[直接描画サンプル](samples/Lumyte.UI.Sample/README.md)／[描画の設計](docs/adr/ui/UI-0004-drawing-and-clipping.md)／[入力の設計](docs/adr/ui/UI-0005-pointer-dispatch-and-capture.md)
- [アニメーションシステムの設計](docs/adr/animation/ANIMATION-0001-animation-system.md)
- [実行基盤のAPIと使い方](src/Core/Lumyte.Execution/README.md)／[Source generator](src/Core/Lumyte.Execution.Generators/README.md)／[実行基盤・DIサンプル](samples/Lumyte.Execution.Sample/README.md)／[実行基盤の設計](docs/adr/core/CORE-0003-execution-and-jobs.md)
- [汎用状態機械の設計](docs/adr/core/CORE-0001-state-machine.md)／[利用例](samples/Lumyte.StateMachines.Sample/README.md)
- [アニメーション状態機械の構築とイベント時刻](docs/adr/animation/ANIMATION-0003-animation-execution-and-event-time.md)／[利用例](samples/Lumyte.Animation.StateMachine.Sample/README.md)
- [形式に依存しない補間・値ソースの合成](docs/adr/animation/ANIMATION-0004-format-independent-value-sources.md)
- [アニメーションの利用例](samples/Lumyte.Animation.Sample/README.md)

ソリューションは `Lumyte.slnx` を使用します。Falloutの`Test` targetで、
`tests/` 内のソリューションに登録されたテストを実行します。
CI は Linux／Windows の x64／aarch64 で、mise セットアップ後に同じFalloutの検証・ビルド・テスト工程を実行します。
すべての push と PR を対象に、TRX 形式の結果を構成ごとに保存します。

- [デバイス補正・仮想デバイスの設計](docs/adr/input/INPUT-0002-processing-and-recognition.md)
- [アクション加工・認識・入力管理の設計](docs/adr/input/INPUT-0003-actions-and-contexts.md)

診断の実通信: [サーバー起動](src/Diagnostics/Lumyte.Diagnostics.Server/README.md)、[ゲーム側サンプル](samples/Lumyte.Diagnostics.Remote.Sample/README.md)、[検証結果](docs/diagnostics/communication-verification.md)。

診断サーバーの[同梱Web UI](src/Diagnostics/Lumyte.Diagnostics.Server/README.md#web-ui)はサーバーのURLから利用できる。[配信・認証・更新の設計](docs/adr/diagnostics/DIAGNOSTICS-0002-server-hosted-ui.md)を参照する。

診断画面の見た目は [GitHub Pagesの画面プレビュー](https://ikihiki.github.io/Lumyte/) でmain・PRごとに確認できます。
