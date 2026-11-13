# ADR-GRAPHICS-0035: C# の固定 wire 配置を shader 引数と転送の正本にする

- 状態: 採用
- 日付: 2026-11-13
- 基準コミット: `ef61258f27ec0f19240a5333d4388f1a3ac9de50`
- 変更関係: 一部変更
- 変更元: [引数への接続](GRAPHICS-0009-shader-argument-binding.md#生成codecとbackendの接続)の field 名による値の配置と target layout への実行時 repacking
- 継続する判断: logical GPU 参照、登録表の所属・型・寿命、利用者による GPU 同期、明示 copy と command-local transfer metadata

## 背景

shader data を作る C# 利用者は、CPU の固定配置を shader と共有し、連続した数値をそのまま転送したい。基準 SHA の `src/Graphics/Lumyte.Graphics.Shared/ShaderDataLayout.cs` の `Pack` は、snapshot の field 名で reflected offset を検索して値を移すため、C# に配置の正本がなく、転送時に field 単位の探索と並べ替えが入る。

[引数への接続](GRAPHICS-0009-shader-argument-binding.md#snapshot寿命失敗時)の snapshot と明示 transfer の所有契約は、この配置方法とは独立に維持できる。opaque GPU 参照を含む logical C# struct は、その CLR memory を GPU wire とみなせない。

## 決定

GPU wire の byte offset を C# の宣言と生成 codec が決め、shader はその配置へ一致させる。generic serializer 自体から field 名を GPU 配置へ解決する機能を削除し、固定 offset の型・範囲・schema を検査して、指定された位置だけへ値と backend 所有の参照 wire を書く。

### 利用者の変更前・変更後

通常利用者の `SetArguments(in arguments)`、mapped staging の `CopyFrom(values)`、明示 copy、submit、完了待ち、解放の呼び方は変わらない。partial 型の生成 codec は C# の member 順を四 byte 単位の固定 wire とし、数値の長さは int/uint/float=4、Vector2=8、Vector3=12、Vector4=16、Matrix4x4=64、opaque 参照=16 byte とする。入れ子は開始 offset を加えて平坦化し、explicit Size の末尾 padding は次の member の前に予約する。

手書き codec の作成者は、従来の名前だけの writer を固定 offset の writer へ移行する。次は同じ logical 値と参照を渡す抜粋であり、登録表の materialization と寿命管理は省略している。

```csharp
// 変更前: shader reflection が field 名から配置先を選ぶ。
writer.WriteValue("Value", Value);
writer.WriteReference("Texture", Texture);

// 変更後: C# が byte offset を明示し、名前は診断用だけに残す。
writer.WriteValue(0, "Value", Value);
writer.WriteReference(16, "Texture", Texture);
```

生成 codec に明示 gap が必要なら logical member に `ShaderMemberOffsetAttribute` を付ける。offset は containing wire の先頭相対で、入れ子を含め負値、重複、overflow、宣言順を逆転する配置を生成時に拒否する。explicit-layout C# 型では `FieldOffset` が優先する正本で、両属性の矛盾は診断する。shader の位置に合わせて実行時に値を並べ直す fallback は設けない。

```csharp
public readonly partial record struct Inputs(
    float Value,
    [property: ShaderMemberOffset(16)] IGpuRef<IGraphicsTextureView> Texture) : IShaderArguments;
```

C# の `WireSizeInBytes` を snapshot の `SizeInBytes` に保持し、shader stride と不一致なら拒否する。generator は data の固定末尾と root の 16 B 境界から stride を生成する。手書き codec の既定値 0 は明示 offset の末尾から同じ C# 規則で推定し、末尾 padding が必要なら型で stride を明示する。shader reflection から stride を選ぶことはしない。logical data と uniform root の配置が異なる場合は、それぞれの固定 C# wire を宣言する。

### 数値だけの直接転送

参照を含まない unmanaged 型は、`StructLayout(LayoutKind.Explicit, Size = ...)` と `FieldOffset` を持つ `IShaderNumericWireData` として明示 opt-in できる。generator は実 C# instance field と backing field から immutable wire metadata を生成する。backend は native allocation 前に managed stride、実 field の全件・型・offset・長さ・非重複と shader ABI を一度だけ照合し、数値 span の連続 bytes を直接転送する。宣言順や unmanaged sizeof だけから互換性を仮定しない。

```csharp
[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly partial record struct NumericElement(
    [field: FieldOffset(0)] Vector4 Value) : IShaderNumericWireData;

// generated metadata と shader ABI の admission 後は field ごとの pack をしない。
staging.CopyFrom(elements);
```

不一致の shader/backend alignment は buffer 作成時に拒否する。互換性が必要なら C# と shader に明示的に一致する別の wire 宣言を用意する。数値 snapshot は入力 span と返した upload bytes とは別の immutable numeric-only owner を保持し、一つの snapshot がその bulk call 全体を保持し得る。GPU completion と command disposal 前の staging/pool 再利用は従来どおり禁止する。

### opaque 参照を含む wire

`IShaderArguments` / `IShaderData` の logical struct と GPU wire を区別する。`IGpuRef` interface/object の CLR 表現を blit せず、固定 offset codec が logical identity を capture し、table materialization 後に従来の backend encoder が既知の位置へ opaque 参照を serialize する。GPU address と整数の偽参照をアプリに作らせない。

全 capture が成功してから pack し、custom codec と reference callback を再実行しない。copy bytes の書込み成功前に transfer metadata を公開しない。member offset・型・byte 長・complete count・重複と bounds、reference kind・table 所属・generation・execution usage、range・stride・lifetime の検査を維持する。診断名を変更しても GPU 配置は変わらず、未知の名前を実行時に解決する入口はない。

### 公開 API と契約の差分

```diff
 namespace Lumyte.Graphics.Abstractions
 {
+    public interface IShaderArguments
+    {
+        static virtual int WireSizeInBytes => 0;
+    }
+    public sealed record ShaderValueSnapshot
+    {
+        public int SizeInBytes { get; init; }
+    }
     public interface IShaderValueWriter
     {
-        void WriteValue<T>(string path, in T value) where T : unmanaged;
-        void WriteReference<T>(string path, IGpuRef<T>? value);
+        void WriteValue<T>(int offset, string path, in T value) where T : unmanaged;
+        void WriteReference<T>(int offset, string path, IGpuRef<T>? value);
     }
-    public sealed record ShaderValue(string Path, Type ValueType,
+    public sealed record ShaderValue(int Offset, string Path, Type ValueType,
         ReadOnlyMemory<byte> Data, object? Reference, bool IsReference);
     public interface IShaderDataLayout
     {
-        string ReferenceKind(string path);
+        string ReferenceKind(int offset);
     }
+    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
+    public sealed class ShaderMemberOffsetAttribute(int offset) : Attribute
+    {
+        public int Offset { get; }
+    }
+    public interface IShaderNumericWireData : IShaderData
+    {
+        ShaderNumericWireLayout NumericWireLayout { get; }
+    }
+    public sealed class ShaderNumericWireLayout
+    {
+        public ShaderNumericWireLayout(int stride, params ShaderNumericWireMember[] members);
+        public int Stride { get; }
+        public IReadOnlyList<ShaderNumericWireMember> Members { get; }
+    }
+    public sealed record ShaderNumericWireMember(string Path, Type ValueType, int Offset, int Size);
 }
 namespace Lumyte.Graphics.Shared
 {
     public sealed partial class ShaderDataLayout
     {
-        public void ValidateValue(string path, Type type);
-        public void ValidateReference(string path, string kind, string? target = null);
+        public void ValidateValue(int offset, Type type);
+        public void ValidateReference(int offset, string kind, string? target = null);
+        public void ValidateNumericWireData<T>() where T : struct, IShaderData;
+        public byte[] Pack<T>(ReadOnlySpan<T> source,
+            Func<ShaderValue, string, byte[]> reference, out ShaderValueSnapshot[] snapshots)
+            where T : struct, IShaderData;
     }
 }
```

## 検討した代替案

- 数値描画だけを高速化し、generic serializer の名前解決を残す: 配置の正本が二つになり、名前から位置を推測する機能が残るため選ばない。
- shader の reflected offset に合わせて転送ごとに再配置する: C# の固定 wire と異なる配置を暗黙に受理するため選ばない。
- managed な logical GPU 参照を含む struct 全体を blit する: CLR object/interface と backend opaque wire は異なり、登録表の identity と所属を保てない。

## 結果と影響

C# は配置を明示し、shader との不一致は実行前のエラーとなる。手書き codec と public snapshot の作成コードは offset を追加する破壊的移行が必要であり、旧名-only overload は残さない。数値 wire 型は直接 span upload を使い、参照を含む型は固定 offset codec による capture と opaque 変換を使う。

検証は全 target の実 shader layout、全数値型と整数 bit pattern、入れ子と明示 padding、未知の診断名で配置が変わらないこと、旧 API 不在、誤 offset/型/stride/重複/未初期化と spoof manifest の拒否、handwritten codec の once-only と例外、古い snapshot、partial transfer・未submit・Graph 宣言・completion-safe reuse を含む。generator を含む NativeAOT と Browser trimming の検証で field metadata preservation を確認する。
