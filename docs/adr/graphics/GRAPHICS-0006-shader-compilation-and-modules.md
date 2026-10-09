# ADR-GRAPHICS-0006: Slangのコンパイル成果物とシェーダーモジュール

- 状態: 採用
- 日付: 2026-10-09

## 背景

共通source言語をSlangとし、offline buildとonline compilationから同じ成果物をGPU moduleへ渡せるようにする。生成WGSLをコミットせずDLLに埋め込み、backendのcode表現とreflectionを対応させる必要がある。

## 決定

### 公開API

共通契約はGraphics.Abstractions、Slang processによるcompiler実装はGraphics.Shadersに配置する。backendがIGraphicsShaderを直接実装する。GPU module確保とsource compileを分ける。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicDevice
     {
+        // artifactのtargetを照合してmoduleを確保。source compile・GPU命令は行わない。
+        // target不一致はArgumentException、解放済みdeviceはObjectDisposedException。
+        IGraphicsShader CreateShader(ShaderArtifact artifact);
     }
     public sealed record DeviceCaps
     {
+        public ShaderTarget ShaderTarget { get; init; }
     }
+    public enum ShaderTarget { Wgsl, SpirV }
+    public enum ShaderStage { Vertex, Fragment, Compute }
+    public sealed record ShaderCompilationDesc
+    {
+        public required string Source { get; init; }
+        public string EntryPoint { get; init; } = "main";
+        public ShaderStage Stage { get; init; } = ShaderStage.Compute;
+        public ShaderTarget Target { get; init; } = ShaderTarget.Wgsl;
+    }
+    public interface IShaderCompiler
+    {
+        // Slang sourceからcodeとtarget別reflectionを作る。deviceを必要としない。
+        // compiler失敗はdiagnosticを含むInvalidOperationException。
+        Task<ShaderArtifact> CompileAsync(ShaderCompilationDesc desc, CancellationToken cancellationToken = default);
+    }
+    public sealed class ShaderArtifact
+    {
+        // codeをコピーし、target・stage・entry・code header／UTF-8とJSONを検証する。
+        public ShaderArtifact(ShaderTarget target, ShaderStage stage, string entryPoint, ReadOnlySpan<byte> code, string reflectionJson);
+        public ShaderTarget Target { get; }
+        public ShaderStage Stage { get; }
+        public string EntryPoint { get; }
+        public string ReflectionJson { get; }
+        // 呼び出し側が変更してもartifactは変化しない新しいコピー。
+        public byte[] GetCode();
+        // DLL resourceからcodeと対応reflectionを読む。欠落はInvalidOperationException。
+        public static ShaderArtifact LoadEmbedded(Assembly assembly, string resourcePrefix, ShaderTarget target, ShaderStage stage, string entryPoint = "main");
+    }
+    public interface IGraphicsShader : IDisposable
+    {
+        public ShaderArtifact Artifact { get; }
+    }
 }
```

### Offlineとonline

Slang sourceだけをコミットする。offline buildは固定したSlang compilerでtarget別codeとreflectionをobj内へ生成し、同じDLLのEmbeddedResourceへ埋め込む。実行時はDLLから読み、code単独の外部ファイル配布やsourceの再compileを必要としない。生成WGSL・SPIR-V・reflectionはコミットしない。

online compilerはSlang executableを呼び、sourceと出力を一時directoryへ置く。引数は構造化して渡し、shellを介さない。終了code・stdout／stderrを回収し、失敗diagnosticを例外に含める。キャンセル時はprocess treeを終了して完了を待ち、一時出力を回収する。compiler instanceはdeviceやnative moduleを所有しない。browserなどprocess実行できない環境ではhostでcompileしたartifactを渡すかoffline artifactを使用する。

同じtarget、entry、stageとrow-major matrix方針をoffline／onlineで使う。reflectionはそのtargetの生成codeとセットで保持する。module作成はnative codeのvalidationやdevice errorsも受けるが、entryのpipeline互換性はpipelineの契約で扱う。

### GPU参照とreflection

[Argument Table](GRAPHICS-0005-argument-tables-and-gpu-references.md)の不透明IGpuRefをshader dataへ渡す際、serializerはSlang reflectionでfield path・offset・strideを検証し、CPU metadataとwire bytesを対応付ける。backendはnative address／descriptorとSlang helperのtarget別表現で実resourceを解決する。実addressやdescriptor番号を利用者が計算するAPIは設けない。

artifactのreflectionを基盤にするが、shader data serializer、GPU参照helper、root引数、binding planとcommandへの接続はそのAPIの設計で扱う。今回のmodule APIだけでmaterial構造体やbindingの自動生成を定義しない。applicationのbuffer内容のstructをAbstractionsへ置かない。

### 所有と同期

shaderはdeviceの子resource。shaderが残るdeviceのDisposeは拒否し、shader Disposeはidempotent。artifactはCPU dataでnative moduleとは独立に生存する。必要なCPU／GPU同期は利用者が管理し、内部lock・アトミックカウンター・InternalsVisibleToを設けない。DisposeでGPU完了待機を挿入しない。

## 検討した代替案

- WGSLを直接手書き・コミット: 共通Slang sourceと成果物の対応を維持するため採用しない。
- module作成時に常にcompile: offline buildとDLL埋め込みの実行経路を保つため分離する。
- GPU参照を生addressとして公開: backend間の表現差と追跡を隠すため不透明参照を使う。

## 結果と影響

同じartifact APIでofflineとonlineの成果物をnative shaderへ渡せる。buildにはSlang executableが必要で、online compilationにも対応hostのcompilerが必要となる。compiler管理はmise、native moduleの詳細は各backend READMEで扱う。

## 検証方針

shared sample projectのSlang sourceをoffline compileしDLLへ埋め込み、共通APIだけでロードとmodule確保を検証する。native testsではIShaderCompiler経由のonline compilationと不正source・キャンセルも確認する。GPU／Wasmテストは既存CIへ組み込む。module確保はdispatchや描画結果の検証とは分ける。
