# Lumyte.Graphics.Shaders

Slang のオンラインコンパイラー実装です。共通契約 `IShaderCompiler` と `ShaderArtifact` は Graphics.Abstractions に置き、GPU デバイスを必要としません。Slang は mise の固定バージョンを使います。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;

IShaderCompiler compiler = new SlangShaderCompiler();
ShaderArtifact artifact = await compiler.CompileAsync(new ShaderCompilationDesc
{
    Source = source,
    EntryPoint = "main",
    Stage = ShaderStage.Compute,
    Target = device.Caps.ShaderTarget,
}, cancellationToken);
using IGraphicsShader shader = device.CreateShader(artifact);
```

`SlangShaderCompiler(string compilerPath = "slangc")` は PATH または明示した実行ファイルを使います。source は単一の Slang translation unit とし、include path・define・複数 entry の設定はこの API の対象に含めません。`CompileAsync` は `-entry`、`-stage`、`-target`、`-matrix-layout-row-major` と `-reflection-json` を指定し、WGSL または SPIR-V と対応する反射情報を返します。

プロセスは shell を介さず起動し、標準出力と標準エラーを並行回収します。Slang の失敗は診断を含む `InvalidOperationException`、起動できない場合は OS の例外、キャンセルは `OperationCanceledException` です。キャンセルした子プロセスの終了を待ち、一時ディレクトリを削除します。CPU／GPU の同期は利用側が管理します。

ブラウザーでプロセスを起動する機能は提供しません。ブラウザーでは DLL のオフライン成果物、またはホストでオンラインコンパイルして渡した成果物を使います。

## オフラインコンパイル

アプリのプロジェクトから [Slang.targets](../../../tools/shaders/Slang.targets) を import します。

```xml
<ItemGroup>
  <SlangShader Include="Shaders/increment.slang"
               ResourceName="MyApp.Shaders.increment"
               EntryPoint="main" Stage="compute" />
</ItemGroup>
<Import Project="../../tools/shaders/Slang.targets" />
```

`ResourceName` は source ごとに重複しない値を指定します。entry と stage の既定値は `main` と `compute` です。compiler の変更は `SlangCompilerPath` で指定できます。

ビルドは `obj/<configuration>/<framework>/Slang/` へ WGSL と SPIR-V を生成し、それぞれの反射情報と一緒に DLL に埋め込みます。source、project、targets、compiler のタイムスタンプを入力として増分ビルドし、clean の FileWrites に出力を登録します。各 source は自己完結するものとします。生成 WGSL・SPIR-V・JSON はコミットしません。

```csharp
ShaderArtifact artifact = ShaderArtifact.LoadEmbedded(
    typeof(Program).Assembly, "MyApp.Shaders.increment",
    device.Caps.ShaderTarget, ShaderStage.Compute);
```

resource 名は `<prefix>.wgsl`／`<prefix>.spv` とそれぞれの `.reflection.json` です。stage と entry はコンパイル時の設定を指定します。`ShaderArtifact` は入力 bytes をコピーし、`GetCode()` もコピーを返します。UTF-8／SPIR-V header と JSON object を検証しますが、shader 言語の意味解析はコンパイラーと GPU API が行います。反射情報から GPU 参照を配置する serializer と pipeline／command API は別の設計範囲です。
