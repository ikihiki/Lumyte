# ADR-GRAPHICS-0001: グラフィックデバイスの共通契約と機能・上限の取得

- 状態: 採用
- 日付: 2026-10-07
- 更新日: 2026-10-08

## 背景

Lumyte の描画コードが、利用するバックエンドの機能と上限を確認できる共通契約を定める。バックエンドごとに生成・初期化の条件が異なるため、生成済みデバイスの情報取得と、バックエンド固有の生成 API を分ける。

インターフェースと、それを実装するために必要な関連型は、バックエンドに依存しない専用プロジェクトに配置する。今回のデバイス契約は Caps の取得を扱い、resource の生成や command の送信は後続の設計対象とする。

## 決定

### 配置と依存関係

プロジェクト・パッケージ・名前空間を `Lumyte.Graphics.Abstractions` とし、[ADR-0002](../0002-repository-layout.md) の配置規則に従って `src/Graphics/Lumyte.Graphics.Abstractions/` に配置する。

IGraphicDevice などバックエンドが実装するインターフェースと、その公開契約に必要な DeviceCaps・機能 flags・共通のデータ型を Abstractions に置く。バックエンド固有の型・native handle・生成設定・実装を参照しない。今後追加する resource や command のインターフェースも、それぞれの設計で共通契約と実装を分ける。

バックエンドは Abstractions を参照し、具象デバイスが IGraphicDevice を直接実装する。利用側も Abstractions を参照し、生成済みのデバイスをこのインターフェースで受け取る。共通 GraphicsDevice class や、内部 driver へ処理を転送する device wrapper は置かない。

アプリケーションの起動部分が選択したバックエンドの固有 API でデバイスを生成する。Abstractions に共通 factory、backend の列挙・自動選択、共通 DeviceDesc を設けない。バックエンドの具象型を生成する箇所だけ、そのプロジェクトへ依存する。

### 公開 API 一覧

比較元は origin/main。以下は追加する公開契約の設計宣言。

```diff
+namespace Lumyte.Graphics.Abstractions
+{
+    // バックエンドの具象デバイスが直接実装する。
+    // 今回の共通操作は機能・上限の取得のみ。
+    public interface IGraphicDevice
+    {
+        // 実際に利用可能な機能と上限の immutable snapshot。
+        // 読み取りで native query、allocation、GPU work を行わない。
+        public DeviceCaps Caps { get; }
+    }
+
+    // バックエンドが生成時に実値を設定する、非所有の情報型。
+    // init 後は変更せず、設定要求や native handle を保持しない。
+    public sealed record DeviceCaps
+    {
+        // 共通の意味へ変換した機能 flags。未知の native flags を混ぜない。
+        public GraphicsFeatures Features { get; init; } = GraphicsFeatures.None;
+        // byte 単位。0 はその用途を利用できない。
+        public ulong MaxBufferSize { get; init; }
+        public ulong MaxStorageBufferBindingSize { get; init; }
+        // resource 数／要素数の上限。0 は対応なし。
+        public uint MaxTextureDimension2D { get; init; }
+        public uint MaxColorAttachments { get; init; }
+        public uint MaxSampledTexturesPerStage { get; init; }
+        public uint MaxSamplersPerStage { get; init; }
+        public uint MaxUniformBuffersPerStage { get; init; }
+        public uint MaxStorageBuffersPerStage { get; init; }
+        public uint MaxComputeInvocationsPerWorkgroup { get; init; }
+        // byte 単位の正数。1 は追加の alignment 制約なし。
+        // 要素数や SizeInBytes を暗黙に補正する値ではない。
+        public uint CopyBufferOffsetAlignment { get; init; } = 1;
+        public uint CopyBufferSizeAlignment { get; init; } = 1;
+        public uint CopyBytesPerRowAlignment { get; init; } = 1;
+        public uint StorageBufferOffsetAlignment { get; init; } = 1;
+    }
+
+    // 同じ値は各バックエンドで同じ機能を表す。
+    // 機能の有無と、対応する操作 API の提供範囲は区別する。
+    [Flags]
+    public enum GraphicsFeatures
+    {
+        None = 0,
+        IndirectDraw = 1,
+        AnisotropicFiltering = 2,
+        DepthBiasClamp = 4,
+        MeshShader = 8,
+    }
+}
```

IGraphicDevice には生成・resource・pipeline・shader・command・Submit の API を追加しない。解放操作や IDisposable の継承も今回の共通契約には含めず、具象デバイスの所有者がバックエンド固有の契約に従って寿命を管理する。

Caps は要求した値ではなく、生成したデバイスで利用できる実値を表す。同じデバイスの読み取りでは、変更しない snapshot を返す。呼び出しごとに native 機能問い合わせや情報型の allocation を行わない。利用側で record のコピーを作っても、デバイスが公開する snapshot は変更されない。

機能・上限は共通の意味で定義し、バックエンドの内部 enum や native flags を公開しない。機能 flags があっても、その機能を使う操作 API が今回の範囲に含まれることは意味しない。上限と alignment の対応付け・妥当性は snapshot を公開する前にバックエンドが確認する。GPU address、native handle、adapter identity、物理 binding slot は Caps に含めない。

### 文書の責務

ADR には共通契約、配置、依存方向と設計判断を記録する。バックエンド固有の生成 API、adapter／instance の選択、初期化・解放手順、native binding、依存パッケージ、対応環境、実装上の制約は、各バックエンドプロジェクトの README.md に記載する。

バックエンドの README を固有 API と使い方の正本とし、同じ宣言や手順をこの ADR に複製しない。固有 API の例はこの ADR に掲載しない。

### 利用例

生成済みデバイスを受け取る側は Abstractions だけに依存して情報を読む。

```csharp
using Lumyte.Graphics.Abstractions;

static ulong GetBufferLimit(IGraphicDevice device)
{
    return device.Caps.MaxBufferSize;
}
```

デバイスを生成する起動部分は、選んだバックエンドの README に従う。生成の失敗、キャンセル、解放済みデバイスの扱いはその固有契約に従い、情報取得のための共通 factory や同期処理は追加しない。

## 検討した代替案

### 共通契約の配置を分散させる

インターフェースの実装に必要な型の参照先が分かれ、共通契約と実装の依存境界が曖昧になる。バックエンドが実装する関連の契約は Graphics.Abstractions にまとめる。

### 共通 factory と GraphicsDevice wrapper を置く

生成を統一できるが、バックエンド固有の初期化条件を共通層へ持ち込む。具象型が IGraphicDevice を直接実装し、生成は固有 API に任せる。

### Resource 生成や送信も同時に設計する

デバイスの情報取得と、resource・command の未確定の契約が結合する。今回の IGraphicDevice は Caps の取得に絞り、それ以外は各責務の設計で追加を判断する。

### バックエンドの手順と API 例を ADR に置く

共通の設計判断と実装に応じて変わる使用手順が混在し、README と説明が重複する。固有の詳細は各バックエンドプロジェクトの README で管理する。

## 結果と影響

- 利用側とバックエンドが Graphics.Abstractions の同じ契約を参照できる。
- 生成済みデバイスの Caps を、バックエンドに依存せず取得できる。
- 起動と終了処理は選んだバックエンドの固有 API と README に依存する。
- 今回の IGraphicDevice だけでは resource の生成や描画・送信を行わない。
- 固有の手順と制約はバックエンドの実装変更に合わせて README を更新する。

## 検証方針

ADR の必須項目、配置・番号・参照先、API diff と宣言コメント、Markdown lint を確認する。公開 interface が Abstractions に属し、Caps 以外の操作や backend 固有の宣言を含まないことを確認する。

実装時は、バックエンドと利用側が同じ Abstractions を参照すること、具象デバイスが直接 IGraphicDevice を実装すること、Caps が同じ意味の機能・上限を表すことを検証する。読み取りで allocation や native query を繰り返さず、利用側のコピーがデバイスの snapshot に影響しないことも確認する。

## 別途決定する事項

- Format ごとの機能、追加の上限、GraphicsFeatures の拡張。
- resource・GPU 参照・command・shader・送信と完了の共通契約。
- Runtime と Renderer の配置と、デバイスを受け渡す箇所。

## 参考資料

- [ADR の書き方と運用](../0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](../0002-repository-layout.md)
- [NoGraphicsAPI 設計比較](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/docs/no-graphics-api-comparison.md)
