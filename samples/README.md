# サンプル

入口は `Lumyte.Samples` です。リポジトリの [開発環境](../docs/development-environment.md) を有効化して実行してください。
ランチャーは選んだプロジェクトだけをビルドします。GPU、OpenAL、Wasm の依存をすべて導入しなくても managed の例から始められます。

```sh
dotnet run --project samples/Lumyte.Samples
dotnet run --project samples/Lumyte.Samples -- --list
dotnet run --project samples/Lumyte.Samples -- --sample graphics
dotnet run --project samples/Lumyte.Samples -- --sample graphics --backend vulkan -- caps
dotnet run --project samples/Lumyte.Samples -- --sample graphics --backend warp -- suite
dotnet run --project samples/Lumyte.Samples -- --sample audio-execution --backend openal -- --host --di
```

指定されていない sample / backend は [Spectre.Console の SelectionPrompt](https://spectreconsole.net/console/prompts/selection-prompt/) で選びます。候補が一つなら自動選択します。backend だけの指定も可能です。
Graphics の native host は続いて exercise を選びます。すべて指定すればプロンプトは出ません。
非対話端末で選択が不足した場合、入力待ちにせず終了コード 1 と使い方を返します。Windows 専用の backend は他の OS では選択できません。
ドライバーや native library の有無は実際の起動時に確認され、失敗時に別 backend へ切り替えません。

ランチャーの `--configuration Release` で子プロジェクトの構成を変更します（既定 Debug）。最後の `--` 以降は個別サンプルにそのまま渡します。
子プロセスの終了コードを返し、Ctrl+C で子プロセスツリーを停止します。個別 csproj の直接実行も引き続き利用できます。

## 役割と読む順序

| 役割 | ID | 内容 / backend |
| --- | --- | --- |
| 基礎 | `signals` | 値の購読と寿命 / managed |
| 基礎 | `composition` | 宣言的な構築と生成 factory / managed |
| 基礎 | `state-machines` | ガード・優先順位・型付き遷移 / managed |
| 基礎 | `animation` | 時計・Timeline・値とイベントの取得 / managed |
| 基礎 | `resources` | 依存関係・ロード・解放 / managed |
| 基礎 | `execution` | GPU/Audio を使わない所有スレッドと配送 / managed |
| 基礎 | `diagnostics` | サーバー不要の診断操作 / managed |
| 基礎 | `window` | OS window の生成・イベント・終了 / windows |
| 統合 | `animation-state-machine` | アニメーション完了と遷移 / managed |
| 統合 | `input` | アクション・認識・永続設定 / managed |
| 統合 | `audio-execution` | 実音声デバイスと Execution / openal, webaudio |
| 統合 | `diagnostics-remote` | 診断サーバー接続 / http, magiconion |
| 統合 | `diagnostics-ui-game` | UI操作Agent（別サーバーとGameTokenが必要） / http, magiconion |
| 統合 | `diagnostics-ui-operator` | UI操作Operator（OperatorTokenが必要） / http |
| 描画 | `ui` | UI記録・offscreen・Window・Browser / record-only, vulkan, wgpu, native, webgpu |
| 統合 | `ui-popup` | Hosted Popupの操作と合成描画 / none, vulkan, wgpu |
| 統合 | `ui-text-editing` | Windows TSFと文字編集（font.ttfが必要） / windows |
| 描画 | `graphics-window` | 三角形・resize・Present / d3d12, warp |
| 描画 | `text` | 多言語シェーピングと描画 / webgpu |
| 契約検証 | `graphics` | 共通 GPU API / vulkan, wgpu, d3d12, warp, webgpu |
| 契約検証 | `audio` | clip / voice の状態・所有権 / openal, webaudio |
| 測定 | `pipeline-benchmark` | Vulkan pipeline cache の有無を比較 / vulkan |

基礎は機能ごとに一つ、統合は複数機能の境界を示す例に限定しています。バックエンド違いは同じ論理サンプルにまとめました。
Graphics の長い全契約検証は `suite` にまとめ、普段は `caps` または一つの exercise を選びます。
Browser Graphics は自動検証にも使うため、共通 suite に加えて canvas / 複数 surface を実行します。
Audio の共通契約本体は `Lumyte.Audio.Shared`、Graphics は `Lumyte.Graphics.Shared` に一つだけ置き、host に同じ検証を複製しません。
既存の `Lumyte.Graphics.DeviceCaps.Sample` は Vulkan / wgpu host、`Lumyte.Graphics.Direct3D12.Sample` は D3D12 host という役割です。

## Browser

```sh
dotnet workload install wasm-tools
dotnet run --project samples/Lumyte.Samples -- --sample audio --backend webaudio --port 8000
```

`dotnet publish` の成功後、`artifacts/samples/<sample>/<backend>/wwwroot` を `http://127.0.0.1:8000` で配信します。
表示された URL をブラウザーで開いてください。Ctrl+C で配信を停止します。ポート使用中はエラーになります。
WebAudio の開始ボタンなど、ブラウザーのユーザー操作はページ上で行います。Browser にはコンソール引数を転送しません。
WebGPU 対応ブラウザー、Slang、Parley の native/Wasm ビルドなど、各 host の README に記載された前提条件は引き続き必要です。

## 個別の設定

`diagnostics-remote` は別途起動した診断サーバーと `LUMYTE_DIAGNOSTICS_GAME_TOKEN` が必要です。
接続先と実行秒数は `-- http://127.0.0.1:5000 60` のように渡します。
`diagnostics-ui-game` は別サーバーと `LUMYTE_DIAGNOSTICS_GAME_TOKEN`、`diagnostics-ui-operator` は `LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN` が必要です。
Gameの接続先と描画方式は `-- http://127.0.0.1:5000 none`、Operatorの接続先とPNG保存先は `-- http://127.0.0.1:5000 ui-remote.png` のように渡します。
`ui-text-editing` はWindows専用です。`-- path/to/font.ttf` でフォントを指定してください。
`ui` の `record-only` と `ui-popup` の `none` はGPUなしで実行できます。Vulkan/wgpuはoffscreen、`native`はWindow表示、`webgpu`はBrowser配信です。
`input` は設定ファイルを書き込みます。必要なら `-- path/to/settings.json` で保存先を指定してください。
その他のオプションや native library の導入方法は各サンプルの README を参照してください。

## 拡張

`Lumyte.Samples.Framework` は引数解析・選択・プロセス寿命・Browser 配信を担当します。
`Lumyte.Samples/SampleCatalog.cs` に `SampleDefinition` と対応する `SampleBackend` を登録すると、一覧と対話選択に反映されます。
backend の project path はリポジトリ相対、Arguments は共通引数の前に渡す host 固有の引数です。
別プロセスで実行するため、STA、所有スレッド、native library、Wasm の制約を各 host 内に保てます。
Spectre.Console は native の起動層だけで使い、共通 GPU exercise や Browser assembly には持ち込みません。

```sh
dotnet test tests/Tools/Lumyte.Samples.Tests
```

## 実行確認（2026-11-01）

Windows x64 / .NET SDK 10.0.401 / Edge 156.0.4314.50 / Agility SDK 620 で、上表の17サンプル・25組み合わせを実行しました。
managed、Vulkan、wgpu、Direct3D 12、WARP、WebGPU、OpenAL、WebAudio、HTTP、MagicOnion、Windows window の全経路が成功しています。
Execution / Audio Execution は default・DI・Host・Host+DI の4構成、Vulkan / wgpu は online shader compile も確認しました。
ウィンドウは実 GPU / WARP とも表示・最大化によるリサイズ・描画継続・正常終了を画面とプロセス終了コードで確認しました。
Browser の文字描画は日本語 fallback・bidi・MSDF/MTSDF・COLR・28種の合成を GPU 読み戻しで検証しています。

検証で見つかった RGB10A2 の alpha 丸めに対する過剰に厳密な比較と、Windows に Linux 向け Vulkan フラグを渡していた Browser 検証スクリプトを修正しました。
RGB10A2 は RGB を厳密比較し、alpha 0.5 の量子化結果だけ隣接する2値を許容します。
比較の根拠は [Direct3D の FLOAT → UNORM 変換許容誤差](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm#3.2.3.6%20FLOAT%20-%3E%20UNORM) です。

ローカルの結果とログは `artifacts/sample-validation/results.json` にまとめています。
OpenAL Soft は検証用ディレクトリに配置し、そのプロセスの PATH に追加しました。再実行時にも OpenAL DLL の探索パスが必要です。
この結果は当該 Windows 環境での確認であり、他 OS・全ドライバー・全ブラウザー・Preview SDK の部分プログラム・スピーカーの聴感品質を保証するものではありません。
