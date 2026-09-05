# E3: プロジェクト配置と依存境界

日付: 2026-09-06

## 決定

製品プロジェクトと隣接するテストを、`src/foundation`、`src/platform`、
`src/interaction`、`src/resources`、`src/graphics`、`src/devtools` の下に領域別に配置する。
サンプルは `samples`、ベンチマークは `benchmarks`、Slangコンパイラアダプターは `tools` に配置する。

RenderGraphは独立した `Lumyte.Graphics.RenderGraph` アセンブリとし、小さい
`Lumyte.Graphics` バックエンド契約へ依存させる。GPUコアランタイムは、コンパイル済みプランの実行に必要な
送信と退役処理（retirement）の一部の内部操作だけをRenderGraphへ公開する。CoreからRenderGraphは参照しない。

`Lumyte.Graphics`はバックエンド向けに準備されたIRである`GpuShaderBinary`を所有し、MessagePackへ依存しない。
検証済みの複数ターゲット用パッケージコンテナは`Lumyte.Graphics.Shader`が所有する。その拡張メソッドは、
バックエンドのパイプライン作成を呼び出す前に`IGpuBackend.ShaderCodeFormat`に合うバイナリを選択し、検証する。
これによりDirectX 12、Vulkan、WebGPUへ渡されるのは準備済みIRだけになる。

`Lumyte.DevTools.Protocol`は、AgentとServerが共有するMagicOnionインターフェイスとMessagePack DTOを所有する。
このプロジェクトが参照するMagicOnionパッケージは抽象契約だけとし、ServerからAgentランタイムは参照しない。

## 強制する依存方向

```text
tools/Shader.Offline -> Graphics.Shader -> Graphics
Graphics.RenderGraph ------------------> Graphics
Graphics backends ---------------------> Graphics

DevTools.Agent  -> DevTools.Protocol <- DevTools.Server
DevTools.Agent  -> DevTools
DevTools.Server -> DevTools
```

アセンブリ境界テストにより、GraphicsコアからMessagePackやコンパイラへの依存、RenderGraphによるシェーダーパッケージの所有、
ServerからAgentランタイムへの依存を拒否する。ソリューションのビルドでは、移動したすべてのプロジェクトとビルド時のシェーダーターゲットが
新しい配置から正しく構築できることも確認する。

検証: `dotnet test Lumyte.slnx -m:1 -v minimal`を実行し、24個のテストプロジェクトで
1,018件成功、失敗0件、スキップ0件を確認した。
