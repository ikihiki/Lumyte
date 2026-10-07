# Lumyte

C# を主言語とするゲームエンジン。最初のグラフィックス実装は wgpu-native を既存 .NET バインディング経由で使います。このバックエンドには Lumyte の C++／`.Native` プロジェクトは不要です。

## 最初の wgpu バックエンド

.NET SDK のバージョンは `global.json` に従います。通常の NuGet パッケージ復元で Ahjo.Wgpu と対応 RID の wgpu-native が導入されます。実行には Vulkan／DirectX などの対応ドライバーが必要です。

```sh
dotnet restore Lumyte.sln --source https://api.nuget.org/v3/index.json
dotnet build Lumyte.sln --no-restore
dotnet run --project samples/Wgpu.Headless --no-restore
dotnet test tests/Lumyte.Graphics.Wgpu.Tests --no-restore
```

サンプルは Slang から生成した WGSL を使用し、Compute で配列を倍にして読み戻した後、オフスクリーンの三角形と背景色を描いて pixel を検証します。ウィンドウは不要です。

GPU がない Linux 環境では Mesa の lavapipe を用意し、その ICD を実行プロセスの `VK_DRIVER_FILES` に指定できます。Mesa の shader cache は書き込める場所を `MESA_SHADER_CACHE_DIR` に指定してください。システムのドライバー登録は変更しません。GPU テストは adapter 不在を skip しません。

現段階はネイティブ向けの初期版です。Compute は UInt32 配列の単一 Storage 引数、描画は single-sample RGBA8 の単一 attachment・通常／indexed triangle-list が対象です。GPU アドレスと物理 binding slot は公開しません。resource と command scope を Dispose し、送信完了を観測してから参照先を再利用・解放してください。

Browser、一般的な Slang 引数生成、オンラインコンパイル、depth／stencil、MSAA、window／swapchain と共通 API への統合は未実装です。[初期実装の契約](docs/adr/0008-wgpu-first-backend.md) と [設計 ADR](docs/adr/) を参照してください。

## サンプル WGSL の再生成

既存の開発環境を有効化し、mise で固定した Slang を使います。サンプルの build と run は保存済みの WGSL だけでも行えます。

```sh
python3 tools/shaders/compile-samples.py
python3 tools/shaders/compile-samples.py --check
```

Windows では `python` を使えます。`slangc` が PATH にない場合は `--compiler PATH` を指定してください。[環境のセットアップ](docs/development-environment.md) を参照してください。

## 開発・設計文書

- [開発環境のセットアップ](docs/development-environment.md)
- [ADR の書き方と運用](docs/adr/0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](docs/adr/0002-repository-layout.md)
- [mise による共通開発環境の設計](docs/adr/0003-development-environment.md)
