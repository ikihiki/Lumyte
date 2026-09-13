# Lumyte.Graphics.Portable.Resources.Generators

準備済みの Portable binding schema から、ResourceManager の型付き参照を持つ C# 入力を生成するビルド時ライブラリです。shader のコンパイル、reflection、package の読み込みや GPU オブジェクトの生成は行いません。実行時の `Lumyte.Graphics.Portable.Resources` は別に参照してください。

## 導入

NuGet package を analyzer として参照すると、同梱した `buildTransitive` targets が `PortableGpuResourceInput` をコンパイラの `AdditionalFiles` に渡します。

```xml
<ItemGroup>
  <PackageReference Include="Lumyte.Graphics.Portable.Resources.Generators"
                    Version="1.0.0" PrivateAssets="all" />
  <PortableGpuResourceInput Include="Shaders/Draw.portable.resources.xml" />
</ItemGroup>
```

ソースからの参照では次を使います。`ProjectReference` は NuGet の targets を自動 import しないため、明示的な import も必要です。代わりに `AdditionalFiles` へ直接登録しても生成できます。

```xml
<ProjectReference Include="../Lumyte.Graphics.Portable.Resources.Generators/Lumyte.Graphics.Portable.Resources.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<Import Project="../Lumyte.Graphics.Portable.Resources.Generators/buildTransitive/Lumyte.Graphics.Portable.Resources.Generators.targets" />
```

出力をディスクに保存する場合の既定位置は `obj/{Configuration}/{TargetFramework}/Shaders/Resources/` です。その下の analyzer 名で Native / Portable を区別します。利用側の `EmitCompilerGeneratedFiles` と `CompilerGeneratedFilesOutputPath` の指定を優先します。generator の DLL は package の `analyzers/dotnet/cs/` に配置し、実行時 DLL や shader runtime への逆依存を追加しません。

## 入力と生成 API

`.portable.resources.xml` は対象 shader と同時に準備した group / binding の対応です。1 ファイルが 1 group の入力型を表します。

```xml
<input namespace="Example" name="DrawResources" group="0">
  <field name="Vertices" kind="Buffer" binding="0" />
  <field name="Albedo" kind="Texture" binding="1" />
  <field name="Filtering" kind="Sampler" binding="2" />
</input>
```

生成される `readonly struct DrawResources` は `IGpuBindingInputs` を実装します。constructor は `GpuBufferRef Vertices`、`GpuViewRef Albedo`、`GpuSamplerRef Filtering` を受け取り、参照を同名の get-only property として公開します。`StorageTexture` も `GpuViewRef` を使い、sampled / storage の対応は shader package の group layout に従います。

- `Group` は宣言した group 番号です。
- buffer には `VerticesOffset` / `VerticesLength` の init property が付きます。既定値は offset 0、length `ulong.MaxValue` で、後者は buffer の残り全体を意味します。
- `Write(GpuBindingWriter writer)` は typed buffer / view / sampler を binding 番号とともに writer に渡します。`scope.GetBindings(program, group, inputs)` がこのメソッドを呼び、参照の依存関係と実際の bindings を作成します。

```csharp
// program は loader で作成済み。管理される bindings より長く保持する。
var inputs = new Example.DrawResources(vertices, albedoView, filtering)
{
    VerticesOffset = 256,
    VerticesLength = 1024,
};
var bindings = scope.GetBindings(program, Example.DrawResources.Group, inputs);
using var batch = manager.BeginBatch();
batch.Use(bindings);
var commands = batch.StartCommandRecording();
commands.SetBindings(Example.DrawResources.Group, manager.GetBindingsHandle(bindings));
// pipeline や描画・dispatch の設定は描画実装が行う。
```

入力 struct 自体は所有権を取得しません。bindings を作成するまで元の scope / pin などを保持し、作成後は scope / batch が binding と参照先の寿命を管理します。Portable shader program の module / layout は呼び出し側が保持します。root data と scalar parameter 値は別に用意し、generator は解析、upload、buffer fallback を行いません。

namespace、型名、field 名は C# の識別子を指定し、`@` は XML に付けません。keyword は生成時に escape します。生成メンバーや buffer range property と衝突する名前、重複型・binding、無効な XML は `LPRG001` で診断します。GPU layout の alignment / usage などの検証は runtime に委ねます。

## 検証

隣接する `.Generators.Tests` は analyzer と targets を実際に読み込んで consumer をコンパイルし、公開 ResourceManager と in-memory backend を使って buffer range / texture / sampler の binding entries、keyword とローカル変数を隠す名前を検証します。GPU は不要です。
