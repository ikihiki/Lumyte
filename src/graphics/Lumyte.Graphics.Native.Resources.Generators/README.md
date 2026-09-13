# Lumyte.Graphics.Native.Resources.Generators

準備済みの Native shader 入力メタデータから、ResourceManager の型付き参照を持つ C# 入力を生成するビルド時ライブラリです。shader のコンパイル、reflection、package の読み込みや GPU オブジェクトの生成は行いません。実行時の `Lumyte.Graphics.Native.Resources` は別に参照してください。

## 導入

NuGet package を analyzer として参照すると、同梱した `buildTransitive` targets が `NativeGpuResourceInput` をコンパイラの `AdditionalFiles` に渡します。

```xml
<ItemGroup>
  <PackageReference Include="Lumyte.Graphics.Native.Resources.Generators"
                    Version="1.0.0" PrivateAssets="all" />
  <NativeGpuResourceInput Include="Shaders/Draw.native.resources.xml" />
</ItemGroup>
```

ソースからの参照では次を使います。`ProjectReference` は NuGet の targets を自動 import しないため、明示的な import も必要です。代わりに `AdditionalFiles` へ直接登録しても生成できます。

```xml
<ProjectReference Include="../Lumyte.Graphics.Native.Resources.Generators/Lumyte.Graphics.Native.Resources.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<Import Project="../Lumyte.Graphics.Native.Resources.Generators/buildTransitive/Lumyte.Graphics.Native.Resources.Generators.targets" />
```

出力をディスクに保存する場合の既定位置は `obj/{Configuration}/{TargetFramework}/Shaders/Resources/` です。その下の analyzer 名で Native / Portable を区別します。利用側の `EmitCompilerGeneratedFiles` と `CompilerGeneratedFilesOutputPath` の指定を優先します。generator の DLL は package の `analyzers/dotnet/cs/` に配置し、実行時 DLL や shader runtime への逆依存を追加しません。

## 入力と生成 API

`.native.resources.xml` の byte offset と全体の `size` は、対象 shader と同時に準備した ABI の値です。GPU address は 8 byte、descriptor index は 4 byte の little endian 値です。

```xml
<input namespace="Example" name="DrawResources" size="32">
  <field name="Positions" kind="GpuAddress" offset="0" />
  <field name="Albedo" kind="DescriptorIndex" resource="View" offset="8" />
  <field name="Filtering" kind="DescriptorIndex" resource="Sampler" offset="12" />
  <field name="Tint" kind="Vector" offset="16" size="16" />
</input>
```

生成される `readonly struct DrawResources` の constructor は `GpuBufferRef Positions`、`GpuViewRef Albedo`、`GpuSamplerRef Filtering` を受け取ります。参照は同名の get-only property として公開します。`GpuAddress` は buffer 参照、`DescriptorIndex` は view または sampler 参照です。`resource` を省略した index は `View` になります。

- `ByteSize` は ABI 全体の byte 数です。
- `Retain(GpuResourceBatch batch)` は各参照を `batch.Use` に渡します。コマンド記録を開始する前に呼びます。
- `Write(GpuResourceManager manager, Span<byte> destination)` は参照に対応する address/index の位置だけを書き換えます。Scalar / Vector / Matrix の値と padding は呼び出し側が用意し、その byte を変更しません。渡す span は `ByteSize` 以上必要です。

```csharp
var inputs = new Example.DrawResources(positions, albedoView, filtering);
using var batch = manager.BeginBatch();
inputs.Retain(batch);

Span<byte> root = stackalloc byte[Example.DrawResources.ByteSize];
root.Clear();
// tint などの scalar 値を対応する ABI offset に書き込む。
inputs.Write(manager, root);

var commands = batch.StartCommandRecording();
// pipeline、attachment、barrier などは描画実装が設定する。
// commands.Draw(root, ...);
```

`Write` は所有権を取得しません。scope / pin / batch などを通して参照の寿命を保ち、shader と同じ ABI の入力を使ってください。書き込み先が root data か parameter data かは generator が判断しません。buffer fallback、upload、submit、待機は追加しません。

namespace、型名、field 名は C# の識別子を指定し、`@` は XML に付けません。keyword は生成時に escape します。生成メンバーと衝突する名前、重複型、参照の幅・範囲・重なり、無効な XML は `LNRG001` で診断します。native API が行う GPU 検証は再実装しません。

## 検証

隣接する `.Generators.Tests` は analyzer と targets を実際に読み込んで consumer をコンパイルし、公開 ResourceManager と in-memory backend を使って address / index、scalar byte 保持、batch の寿命保持、keyword とローカル変数を隠す名前を検証します。GPU は不要です。
