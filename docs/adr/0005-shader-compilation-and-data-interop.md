# ADR-0005: Slang を基準とするシェーダーコンパイルと GPU データ受け渡し

- 状態: 提案
- 日付: 2026-10-07

## 背景

[ADR-0004](0004-graphics-library.md) のグラフィックス共通契約から、シェーダーの言語、コンパイル、引数レイアウト、GPU データ参照の解決を独立させる。Slang を基準とし、配布時にコンパイラを必要としないオフラインコンパイルと、編集・特殊化・動的生成に使うオンラインコンパイルを用意する。

利用者は GPU データを参照できる必要があるが、実 GPU アドレス、descriptor index、bind group の slot を知る必要はない。DirectX、Vulkan、WebGPU で参照の物理表現と型レイアウトは異なるため、Native 実装と Slang の機能を組み合わせ、各バックエンドライブラリが実データを受け渡す仕組みを持つ。

Slang はモジュール、interface／generics、リンク時特殊化、ターゲット別コード生成、反射情報を提供する。一方、任意のポインタを WGSL の GPU アドレスへ変換できるものではない。共通の論理スキーマと、ライブラリが管理するバックエンド別の実装・物理レイアウトを分ける必要がある。

## 決定

### 言語、責務、適用範囲

シェーダーのソースと論理データスキーマは Slang を正本とする。HLSL、SPIR-V、WGSL はバックエンド向けの生成物として扱う。利用者は共通の Slang ライブラリ API と生成済み C# データ型を使い、バックエンドのバインディング宣言を自分で管理しない。

| 構成要素 | 責務・依存 |
| --- | --- |
| `Lumyte.Graphics.Shaders` | コンパイル要求、成果物、診断、論理スキーマ、生成 C# API。GPU Core のデバイスやパイプライン生成には依存しない |
| `Lumyte.Graphics.Shaders.Native` | Windows／Linux の Slang コンパイル API を C ABI で包む。オフラインツールとオンライン provider が共用 |
| 各 Graphics バックエンド | ターゲット profile、Slang の対応モジュール、物理レイアウトから pack／bind する処理を提供。DirectX／Vulkan は各 `.Native` 実装へ委譲 |
| Graphics Runtime | フレーム引数、CPU pack と参照の検証・解決、GPU 完了までの保存を管理 |
| オフラインビルドツール／オンライン compiler provider | 同じ要求とコンパイル処理を実行。GPU Core にコンパイラを必須依存として持ち込まない |

成果物・データ参照・結果の共通型は `Lumyte.Graphics` プロジェクトに置き、コンパイル API のプロジェクト `Lumyte.Graphics.Shaders` がこれを参照する。Core から compiler provider プロジェクトへの依存は設けず、型の名前空間と配置プロジェクトを区別する。各バックエンドのコンパイル用 profile と Slang モジュールはデバイスなしで取得可能とする。プロジェクトとパッケージは [ADR-0002](0002-repository-layout.md) の配置・命名規則に従い、実装時に追加する。

### オフラインとオンラインコンパイル

オフライン成果物はビルド時に生成し、ターゲットコードと必要なメタデータを DLL の埋め込みリソースとして配布する。実行時は assembly の manifest resource から読み出して同じ成果物契約へ渡す。生成 WGSL は Git に含めず、ビルドの中間ディレクトリにのみ保存する。Slang ソースを正本とし、コンパイラはビルド環境にだけ必要とする。

両方式に共通する処理は、ソース／依存モジュールの解決 → 型検査・IR → ライブラリ実装との合成・特殊化・リンク → ターゲット別コード生成・反射 → 成果物の検証と保存とする。反射には Slang のコンパイル API を用いる。CLI のコード生成だけから十分な反射情報が得られると仮定しない。

| 方式 | 入力と処理 | 想定用途・配布 |
| --- | --- | --- |
| オフライン | ビルド時に全工程を実行し、使用予定の profile と特殊化ごとに成果物を生成 | 通常の製品配布。ターゲットコードと必要なメタデータだけを含め、Slang コンパイラとソースは必須にしない |
| オンライン | 実行時に Slang ソースまたは互換な事前コンパイル IR を受け取り、同じ工程で成果物を生成 | エディタ、ホットリロード、動的シェーダー、実行時に決まる特殊化。明示的に compiler provider を追加 |
| オフライン IR ＋オンラインリンク | モジュールを事前に IR 化し、実行時に library implementation と特殊化値を合成してターゲットコードを生成 | ソース解析の再利用。最終生成には compiler provider とターゲットの downstream compiler が必要 |

オンラインはアプリケーション実行中のコンパイルを意味し、ネットワーク通信を前提としない。コンパイルは非同期要求として扱い、draw／dispatch 内で暗黙に実行しない。起動時の prewarm、バックグラウンド生成、キャッシュを使用できるようにする。特殊化を必要としない実行時定数の変更はコンパイルせず引数更新で行う。

Slang の IR は最終 GPU コードではなく、コンパイラバージョンをまたぐ永続互換形式として扱わない。互換性が合わなければ利用可能なソースから再生成し、ソースも provider もなければ明示的に失敗させる。

### ターゲットと実行環境

| バックエンド | 成果物 | データ参照の実装方針 |
| --- | --- | --- |
| DirectX | 採用する DirectX のバージョン・Shader Model に対応するコード。DXIL を第一候補とし、最終選択は対応範囲の ADR に従う | descriptor、buffer view、offset、必要なら root の GPU VA を Native 側で構築。シェーダーで任意の VA を間接参照できると仮定しない |
| Vulkan | profile に対応した SPIR-V | device address が利用可能な場合は Native が実アドレスを解決し、Slang の対応モジュールで参照。それ以外は storage buffer と offset／descriptor に解決 |
| WebGPU | WGSL ソースと反射メタデータ | storage buffer と offset、texture／sampler binding に解決。64-bit GPU アドレスや無制限のリソース配列を要求しない |

オフラインコンパイルでも、ドライバーやブラウザによる module 検証と GPU pipeline の生成は実行時に残る。特に WGSL は配布済みの機械語ではない。これを Slang ソースからのオンラインコンパイルと区別する。

Windows／Linux のオンライン provider は Native の Slang API を使用する。必要な downstream compiler の有無も provider の対応 profile に含める。Browser はオフライン生成 WGSL の読み込みを基本経路とし、オンライン用には同じ契約の Wasm Slang provider を用意する方針とする。Wasm 配布・反射 API・メモリ制約・実行方式の成立は実装時に検証し、未導入または対象非対応なら `CompilerUnavailable`／`UnsupportedTarget` を返す。Browser にホストの Native ライブラリや暗黙の外部コンパイルサービスを要求しない。

### 論理スキーマと不透明な GPU データ参照

利用者の C# API は `GpuReference<T>` を型付きの不透明な値として扱う。コンストラクターと内部表現は非公開で、実アドレス、整数、CPU ポインタ、descriptor index への変換・表示を提供しない。任意の bytes から復元できず、永続保存や異なるデバイス・バックエンド間の受け渡しには使用できない。不透明とは表現のカプセル化を意味し、プロセス内の攻撃者に対する秘匿を保証するものではない。

`T` は Slang の論理データ型に対応する登録済みの生成型とする。`unmanaged` な任意の C# 型を渡すだけでシェーダー型と互換になるわけではない。参照の生成元は GraphicsDevice が管理する BufferSlice とし、内部でデバイス、元の領域、型 ID、世代、範囲、アクセス用途を保持する。公開 token のサイズと値に意味を持たせない。

Slang 側では、ライブラリが公開する `GpuRef<T>` と `load`／`store` などの型付きアクセスを使う。利用者は生の `T*`、アドレス算術、参照の内部フィールドを共通コードで扱わない。範囲内の要素アクセスは共通 API として提供し、書き込みは論理スキーマとリソース用途が許可する参照に限る。

各バックエンドライブラリは、共通の Slang interface／generics と対応する実装モジュールを提供する。Slang の composition、specialization、reflection を用いて、各ターゲット用の load／store と引数型を生成する。ターゲット固有機能が必要な部分だけライブラリ内部の interop 機能を使い、その適用箇所と Slang バージョンを管理する。Slang 単体が任意のユーザーのポインタプログラムを自動移植するとは扱わない。

WebGPU の共通経路では、参照フィールドの論理パスごとに有限の resource binding を割り当て、そこに binding された buffer と offset で解決する。利用者が任意の buffer を参照できても、同時参照数・配列構成・パスの使用条件はコンパイル profile とデバイス上限に従う。異なる buffer を任意にたどる循環グラフ、GPU が生成した任意のアドレス、無制限の動的リソース集合は共通経路の保証外とし、必要機能を満たさない場合は診断付きで拒否する。

### Native と Slang の実データ受け渡し

参照先 Buffer の領域／世代／schema は [ADR-0009](0009-buffer-resource-contract.md)、TextureView の subresource／型／access は [ADR-0010](0010-texture-resource-contract.md)、Sampler の category と sampling pair は [ADR-0011](0011-sampler-resource-contract.md) に従う。BindingPlan は texture／sampler の関連 field path を保持し、生成 serializer と backend が適合を検証する。

1. Slang の論理スキーマから C# の引数型とデータ型、スキーマ ID を生成する。共通の利用者 API とターゲットごとの物理レイアウトを分ける。
2. 対象 profile のライブラリモジュールをリンクし、Slang の反射情報から定数の offset／alignment／stride、resource category、binding space、root の表現を取得する。論理フィールドとの対応を BindingPlan に保存する。
3. 利用者が生成 C# 引数型へ通常の値と GpuReference を設定する。Runtime が型、範囲、世代、デバイス所属、アクセス用途を検証する。
4. 生成 serializer が値と参照 token を C ABI 用の論理フィールド列へ変換する。DirectX／Vulkan の各 Native 実装が token を解決し、BindingPlan に従って実 GPU アドレス、descriptor、定数 bytes と root を構築する。Slang が生成した対応コードは同じ plan の表現からデータを読む。
5. WebGPU は同じ論理フィールド列を Browser 実装が pack し、buffer／texture／sampler の bind group と offset を構築する。Browser で Native C++ に処理を委譲することを必須にしない。
6. root、descriptor、定数、参照先と plan を使用した pipeline は GPU 完了まで保持する。再利用と解放は ADR-0004 の Submission に従う。

BindingPlan はコンパイル成果物の内部メタデータであり、利用者がスロットやアドレスを指定する入力ではない。Slang module、Native packer、生成 serializer は library ABI ID を共有する。異なる ABI の組み合わせ、別デバイスの token、失効した参照、範囲外、型違いを pack 前に拒否する。

Managed の引数構造体を `memcpy` して GPU 上の構造体として扱わない。bool、vector、matrix、配列、padding を Slang の反射情報に従って pack する。行列の論理規約は row-major を基準とし、各ターゲットの物理配置と stride は検証・変換する。GPU データ本体も生成 serializer によって target layout に変換して既存 Upload buffer の CPU memory にコピーする。GPU 転送と CommandBuffer の送信は利用者が明示する。

参照を含む GPU データ型は scalar だけの POD と区別し、生成コードが参照フィールドを列挙・解決できるスキーマに限定する。生の token を通常の bytes として Upload する経路は提供しない。ポインタを含まない型も、ゼロコピー可能なのは layout の一致を検証した場合だけとする。

C ABI は opaque handle、固定幅整数、明示レイアウトの POD と field array を使用する。C++ 例外を境界外へ出さず、呼び出し側の Span は呼び出し中にコピーする。コンパイルの非同期処理に渡すソースも要求受付時に所有コピーを作る。コンパイラ所有の reflection pointer や session を成果物へ保存しない。

### 成果物とキャッシュ

`ShaderArtifact` は以下を持つ論理的な配布単位とする。オフラインとオンラインで別形式を設けない。

- ターゲットコード、entry point、stage、threadgroup size、target profile と必須機能。
- 論理スキーマ ID とハッシュ、型情報、特殊化値、ライブラリモジュールの依存情報。
- ターゲット別の反射情報、BindingPlan、library ABI ID、生成 serializer の互換性 ID。
- 成果物形式のバージョン、Slang・downstream compiler のバージョン、コンパイル設定、内容ハッシュ。

キャッシュキーは、ソースと推移的な import／include の内容、entry point と stage、profile／capability、特殊化値、最適化・debug・matrix layout 設定、コンパイラと library ABI のバージョンから構成する。ファイル名や最終更新時刻だけで再利用を判断しない。実 GPU アドレスや実行時 token は成果物・キャッシュに保存しない。ドライバーの pipeline cache は別の寿命と互換性を持つ。

オンラインで schema ID が同じ場合は新しい成果物と pipeline を準備してからフレーム境界で切り替える。schema が変わった場合は既存の生成 C# 型へ自動適用せず、型と利用側の再生成・再構築を要求する。古い pipeline と引数は最後の GPU 使用が完了してから解放する。コンパイル失敗時は診断を返し、既存の正常な pipeline を維持する。

`ShaderArgumentsLayout` を不変の非 generic な共通基底契約とし、`ShaderArgumentsLayout<T>` はこれを継承して生成型との対応を加える。Graphics／ComputePipelineDesc は共通基底を受け取る。コンパイルされた linked program の composition ID、schema、library ABI と BindingPlan の互換性 ID は内部で保持し、利用者へ物理 binding を公開しない。

### 公開 API 一覧

以下は C# の主要シグネチャ案であり未実装である。コンパイル関係と成果物の名前空間は `Lumyte.Graphics.Shaders`、GpuReference と生成データの契約の名前空間は `Lumyte.Graphics` とする。`Result<T>` と GraphicsError は ADR-0004 の共通結果契約に従う。成果物・レイアウトの型定義は GPU Core と compiler provider が共有する契約として配置する。

API は .NET の API review／API diff に倣い、namespace・型・メンバーを C# 宣言でまとめる。`+` は origin/main に対する追加 API、`-` は削除 API、無印は変更の文脈を表す。この PR の main には Graphics API がないため、掲載する宣言は追加として表示する。各ブロックは当該 ADR の対象メンバーの抜粋であり、実装コードではない。説明と検証条件は宣言の `//` コメントに記す。提案と実装済みの区別は ADR の状態と本文に従う。

```diff
+namespace Lumyte.Graphics.Shaders
+{
+    public interface IShaderCompiler
+    {
+        // provider の対応を確認
+        // profile、ソース／IR 入力、反射、特殊化の対応を報告
+        // GPU の Caps とは別
+        ShaderCompilerCapabilities Capabilities { get; }
+
+        // ソース／IR からコンパイル
+        // 対象 profile、entry point、module resolver、特殊化値、設定を明示
+        // 入力を所有コピーし、診断を返す
+        ValueTask<Result<ShaderCompilation>> CompileAsync(ShaderCompileRequest request, CancellationToken cancellationToken);
+    }
+
+    public interface IShaderModuleResolver
+    {
+        // import／include の解決
+        // 内容と論理識別子を返す
+        // 暗黙のネットワーク取得を行わない
+        ValueTask<Result<ShaderSource>> ResolveAsync(string moduleName, CancellationToken cancellationToken);
+    }
+
+    public sealed class ShaderCompilation
+    {
+        // 成果物と診断
+        // warning を保持
+        // 失敗時も Result のエラーに構造化診断を含める
+        public ShaderArtifact Artifact { get; }
+
+        // 成果物と診断
+        // warning を保持
+        // 失敗時も Result のエラーに構造化診断を含める
+        public IReadOnlyList<ShaderDiagnostic> Diagnostics { get; }
+    }
+
+    public sealed class ShaderArtifact
+    {
+        // オフライン成果物の読み書き
+        // 形式、内容ハッシュ、必須メタデータを検証
+        // GPU オブジェクトや token を含まない
+        public static Result<ShaderArtifact> Load(ReadOnlyMemory<byte> data);
+
+        // オフライン成果物の読み書き
+        // 形式、内容ハッシュ、必須メタデータを検証
+        // GPU オブジェクトや token を含まない
+        public ReadOnlyMemory<byte> Serialize();
+
+        // 生成 C# 型の対応を取得
+        // schema と serializer の互換性 ID を検証
+        // BindingPlan の物理詳細は非公開
+        public ShaderArgumentsLayout<T> GetArgumentsLayout<T>() where T : IShaderArgumentsData;
+
+        // データ本体の pack 契約を取得
+        // target の型 layout と生成 serializer を照合
+        // 異なる物理 layout を暗黙に共用しない
+        public ShaderDataLayout<T> GetDataLayout<T>() where T : IShaderData;
+    }
+}
+
+namespace Lumyte.Graphics
+{
+    public sealed class GraphicsDevice : IDisposable
+    {
+        // データ領域を型付きで参照
+        // 登録済みの生成型、領域の schema／layout ID、target stride、範囲、デバイスを検証
+        // 所有権を持たない
+        public GpuReference<T> CreateReference<T>(BufferSlice data) where T : IShaderData;
+
+        // 生成データ型を target layout に pack し、利用者の Upload buffer の CPU memory にコピーする。
+        // 参照フィールドを解決して metadata を登録。GPU コピー、staging 確保、送信は行わない。
+        public void CopyBuffer<T>(BufferSlice destination, ShaderDataLayout<T> layout, ReadOnlySpan<T> values) where T : IShaderData;
+
+        // GPU module の生成
+        // profile・必須機能・ABI を検証
+        // コンパイル provider は起動しない
+        public Result<ShaderModule> CreateShader(ShaderArtifact artifact);
+    }
+}
+
+namespace Lumyte.Graphics.Runtime
+{
+    public sealed class FrameContext
+    {
+        // 定数と参照を pack
+        // 生成 serializer とバックエンドが物理表現を構築
+        // C# オブジェクトのメモリ配置へ依存しない
+        public ShaderArguments CreateArguments<T>(ShaderArgumentsLayout<T> layout, in T values) where T : IShaderArgumentsData;
+    }
+}
```

`IShaderData` と `IShaderArgumentsData` は生成 serializer を持つ型の契約である。参照を含む生成型に `unmanaged` を要求せず、型付き `CopyBuffer<T>` はフィールドを列挙して値と参照を解決し、利用者が確保した Upload buffer の CPU memory に pack する。GPU への転送は利用者が RecordCopyBuffer を記録し、CommandBuffer を Submit する。root 引数は `CreateArguments<T>` で構築する。どちらも単なる marker interface の実装だけでは利用できず、型 ID と生成 serializer の登録を必須とする。CPU pack した staging と明示的な転送先の領域には schema／layout ID と解決した参照の依存情報を記録し、CreateReference と引数 pack で照合する。生の BufferSlice にこれらのメタデータがない場合は型付き参照の生成を拒否する。コンパイル時に確定した参照経路とアクセス用途を BindingPlan に含め、共通経路で表現できない参照グラフを拒否する。

ソース位置、severity、コード、ターゲット、依存 module を ShaderDiagnostic に残す。未対応機能、コンパイラ不在、コード生成失敗、ABI 不一致を区別する。session の並列利用を仮定せず、provider は要求単位の session または直列化を管理する。キャンセル後の結果は公開せず、Native 処理が停止できない場合も終了後に所有リソースを解放する。

## 検討した代替案

### バックエンドごとに HLSL／GLSL／WGSL を手書きする

ターゲット固有の制御は容易だが、データ型、参照の意味、引数レイアウトが分散する。Slang の共通ソースとライブラリ側の実装モジュールを基準にする。

### オフラインのみ、またはオンラインのみを提供する

オフラインのみでは動的生成や実行時特殊化ができず、オンラインのみでは起動時間とコンパイラ配布が必須になる。同じ成果物契約で両方を提供し、利用側が必要な provider を選択する。

### 実 GPU アドレスまたは descriptor index を利用者に渡す

低レベル操作は直接表現できるが、バックエンド固有の表現、寿命、レイアウトの知識が利用側へ広がる。型付きの不透明な参照を受け取り、Native／Slang の対応実装で解決する。

### 全ターゲットで C# と同じ構造体レイアウトを強制する

単純なコピーで済む場合はあるが、resource category、matrix、配列や alignment の規則が異なる。論理スキーマを共有し、反射と serializer によって物理レイアウトを適合させる。

## 結果と影響

- Slang を基準に、オフライン配布とオンライン編集で同じ API・成果物・データ参照の意味を使用できる。
- 利用者は実 GPU アドレスやバインディング番号を識別せずにデータを渡せる。Native 実装と Slang module の ABI 整合性をライブラリが引き受ける。
- バックエンド用モジュール、反射メタデータ、生成 C# 型、serializer、compiler provider の配布・互換性管理が必要になる。
- オンラインコンパイルには待ち時間とメモリ使用量があり、オフラインでも複数 profile・特殊化の成果物が増える。
- 共通の参照モデルは WebGPU の上限を消すものではない。任意ポインタや無制限の動的参照は機能条件として扱う。
- 現段階は設計の記録であり、Native の packer、Wasm provider、全ターゲットのシェーダー実行と性能は未検証である。

## 検証方針

同一 Slang ソースと要求からオフライン／オンラインで生成し、schema、反射情報、BindingPlan の互換性と、GPU 上の結果が一致することを確認する。コードの bytes が常に同一になることは受け入れ条件にしない。

DirectX、Vulkan、WebGPU で、scalar／vector／matrix／配列、型付き参照、テクスチャ・サンプラを含む引数を受け渡し、Compute と描画の Readback を比較する。実アドレス経路と buffer／offset 経路が同じ論理結果を返すこと、失効・別デバイス・型違い・範囲外の参照と ABI 不一致を拒否することを確認する。

依存ソース・特殊化・profile・compiler version・library ABI の変更でキャッシュが失効すること、compiler provider のない配布でも成果物を読み込めること、IR 互換性不一致が明示されることを確認する。ホットリロード失敗時の旧 pipeline 維持、schema 変更の拒否、GPU 完了前の再利用防止とキャンセル時の解放も確認する。

Browser の Wasm provider は成果物・診断・反射の共通契約に従えるか検証する。既存の SPIR-V smoke test だけを各ターゲットの ABI／実行検証の代用にしない。今回の PR では ADR の規約、相互参照、API と責務の整合性を確認する。

## 別途決定する事項

- DirectX の最終バージョンと target profile、共通 Slang 言語機能の一覧と最低 device capability。
- 生成型・スキーマ属性・Slang の load／store API と Native C ABI の全宣言。
- compiler provider の RID、Slang／downstream compiler の配布方法、Browser の Wasm 実行と Worker 連携。
- 成果物の具体的なファイル形式、キャッシュの保存先・容量・破損時の復旧。
- build system／エディタへの統合、ホットリロードの API、オンライン生成時の利用制限。

## 参考資料

- [ADR の書き方と運用](0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](0002-repository-layout.md)
- [グラフィックスライブラリの共通契約](0004-graphics-library.md)
- [Graphics の Desc 型](0007-graphics-descriptors.md)
- [Slang compilation API](https://github.com/shader-slang/slang/blob/master/docs/user-guide/08-compiling.md)
- [Slang reflection](https://github.com/shader-slang/slang/blob/master/docs/user-guide/09-reflection.md)
- [Slang link-time specialization／module precompilation](https://github.com/shader-slang/slang/blob/master/docs/user-guide/10-link-time-specialization.md)
- [Slang target interoperation](https://github.com/shader-slang/slang/blob/master/docs/user-guide/a1-04-interop.md)
- [Slang WGSL target](https://github.com/shader-slang/slang/blob/master/docs/user-guide/a2-03-wgsl-target-specific.md)
- [NoGraphicsAPI shared Slang contract](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/docs/slang.md)
