# Lumyte.Resources.Embedded

DLLの`EmbeddedResource`を、既存のResource Pipelineの入力として読み込みます。読み込み済みの`Assembly`をDIで指定します。

```xml
<ItemGroup>
  <EmbeddedResource Include="Shaders/basic.spv" LogicalName="App.Shaders.Basic.spv" />
</ItemGroup>
```

```csharp
services.AddSingleton(new EmbeddedResourceSourceOptions
{
    Assembly = typeof(App).Assembly,
    ResourceNames = new Dictionary<ResourceId, string>
    {
        [new("shaders/basic.spv")] = "App.Shaders.Basic.spv",
    },
});
services.AddResources("assets", pipelines => pipelines
    .UseSource<EmbeddedResourceSource>()
    .AddSourcePipeline<CpuShader, ShaderStage>());
```

Stageは通常どおり`context.OpenSourceAsync(new("shaders/basic.spv"))`でsnapshotを取得し、`snapshot.OpenReadAsync()`で読み込みます。Stage型はアプリ側の実装です。GPUへの変換には後続のtransformと`Lumyte.Resources.Graphics`を利用できます。ファイルSourceと同じ論理IDを対応付ければ、Pipelineを共通にできます。

`ResourceNames`は論理IDから正確なmanifest resource名への対応です。Sourceの構築時にコピーし、後から辞書を変更しても既存Sourceの内容は変わりません。対応付けのないIDはその文字列をそのままmanifest resource名として使います。名前は大文字・小文字を区別し、ドットやスラッシュを自動変換しません。

snapshotは独立した読み取り専用streamと範囲読み込みを提供し、入力全体のmanaged bufferや一時ファイルを確保しません。RevisionはassemblyのMVIDとmanifest resource名から決まり、読み込み済みの同じDLLでは不変です。取得後のデコード・展開・GPU確保はStage側のmemory scopeへ計上してください。DLLを保持するランタイムのメモリはSource側で重複計上しません。

存在しないリソースのRevisionは`null`になり、snapshot取得は`FileNotFoundException`になります。埋め込みデータを持たない外部リンク形式は対象外です。Sourceは渡されたassemblyを借用し、DLLのロード・差し替え・アンロードを行いません。snapshotとreaderもassemblyを保持するため、collectibleな`AssemblyLoadContext`でも利用中のデータが先にアンロードされることを防ぎます。

埋め込みデータ自身は変更監視を行いません。開発中は`FileResourceSource`とファイル監視、配布時はこのSourceへDIで切り替える構成にできます。
