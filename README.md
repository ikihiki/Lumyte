# Lumyte

C# を主言語とするゲームエンジン。最初のグラフィックス実装は wgpu-native を既存 .NET バインディング経由で使います。このバックエンドには Lumyte の C++／`.Native` プロジェクトは不要です。

## 最初の wgpu バックエンド

ビルド前に mise の Slang を PATH に配置してください。.NET SDK のバージョンは `global.json` に従います。通常の NuGet パッケージ復元で Ahjo.Wgpu と対応 RID の wgpu-native が導入されます。実行には Vulkan／DirectX などの対応ドライバーが必要です。

```sh
dotnet restore Lumyte.sln --source https://api.nuget.org/v3/index.json
dotnet build Lumyte.sln --no-restore
dotnet run --project samples/Wgpu.Headless --no-restore
dotnet test tests/Lumyte.Graphics.Wgpu.Tests --no-restore
```

サンプルは Slang から生成した WGSL を使用し、Compute で配列を倍にして読み戻した後、オフスクリーンの三角形と背景色を描いて pixel を検証します。ウィンドウは不要です。

GPU がない Linux 環境では Mesa の lavapipe を用意し、その ICD を実行プロセスの `VK_DRIVER_FILES` に指定できます。Mesa の shader cache は書き込める場所を `MESA_SHADER_CACHE_DIR` に指定してください。システムのドライバー登録は変更しません。GPU テストは adapter 不在を skip しません。

現段階はネイティブ向けの初期版です。Compute は UInt32 配列の単一 Storage 引数、描画は single-sample RGBA8 の単一 attachment・通常／indexed triangle-list が対象です。GPU アドレスと物理 binding slot は公開しません。Upload は利用者が Upload buffer を生成し、IGraphicsBuffer<T>／BufferSlice<T>.CopyFrom で CPU memory に書いた後、RecordCopyBuffer を記録して Finish／Submit します。CopyFrom／CopyTo は GPU 転送や queue write を行いません。Readback も利用者が CPU destination を確保し、GPU 完了後に IGraphicsBuffer<T>／BufferSlice<T>.CopyTo で読み出します。buffer と command の内部契約は分離しています。resource と command scope を Dispose し、送信完了を観測してから参照先を再利用・解放してください。

テストとサンプルは `Lumyte.Graphics` の共通 API のみを使用します。`Graphics.CreateDevice()` が既定の wgpu 実装を生成し、Core はバックエンドに依存しません。

Browser、一般的な Slang 引数生成、オンラインコンパイル、depth／stencil、MSAA、window／swapchain と広い共通契約の残りは未実装です。[初期実装の契約](docs/adr/0008-wgpu-first-backend.md) と [設計 ADR](docs/adr/) を参照してください。

## オフラインシェーダーのビルド

mise で固定した Slang を有効化してからビルドしてください。共有 MSBuild targets が Slang ソースを obj 内の WGSL にコンパイルし、サンプル／テスト DLL の埋め込みリソースへ格納します。生成 WGSL はコミットせず、実行時は DLL から読み出します。配布先には Slang コンパイラやシェーダーの別ファイルは不要です。

```sh
dotnet build Lumyte.sln -p:SlangCompilerPath=/path/to/slangc
dotnet run --project samples/Wgpu.Headless --no-build
```

SlangCompilerPath の既定値は PATH 上の slangc です。[環境のセットアップ](docs/development-environment.md) を参照してください。

## 開発・設計文書

- [開発環境のセットアップ](docs/development-environment.md)
- [ADR の書き方と運用](docs/adr/0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](docs/adr/0002-repository-layout.md)
- [mise による共通開発環境の設計](docs/adr/0003-development-environment.md)

- [バッファの利用 API とバックエンド契約](docs/adr/0009-buffer-resource-contract.md)
- [テクスチャ・ビュー・転送の利用 API とバックエンド契約](docs/adr/0010-texture-resource-contract.md)
- [サンプラーの利用 API とバックエンド契約](docs/adr/0011-sampler-resource-contract.md)

バッファは `device.CreateBuffer(new BufferDesc<uint> { Count = 8, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload })` のように型と要素数で生成します。`SizeInBytes` は自動計算され、`Slice(offset, count)` も要素単位です。数値型や `unmanaged` struct を許可しますが、Slang の ABI 互換性は別途検証します。具象バックエンドが `IGraphicsBuffer<T>` を直接実装し、共通 `Buffer` ラッパーの確保を省きます。
