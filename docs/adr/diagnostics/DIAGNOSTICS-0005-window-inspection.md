# ADR-DIAGNOSTICS-0005: WindowSystem を診断画面へ明示登録する

- 状態: 採用
- 日付: 2026-10-12
- 基準コミット: `fbc81051cbf0f4aa5d996237543765e02b8cfa5c`（既存判断の記述整理）

## 背景

ウィンドウの問題を調べる利用者は、親子関係や可視性を確認し、表示・終了を要求したい。`WindowSystem` はこれらを管理するが、診断通信へ生のウィンドウや Native ハンドルを渡すと、内部資源の寿命と遠隔操作が結び付いてしまう。

## 決定

接続パッケージ `Lumyte.Diagnostics.Windowing` を使い、利用者が選んだ `WindowSystem` だけを診断画面へ登録する。診断操作は既存の所有スレッド上で実行し、結果には数値や文字列などの値だけを返す。Windowing 本体の依存先は増やさない。

### 利用者の変更前・変更後

この機能を使わない場合、利用者はプロセス内で `WindowNode` を参照して操作する。例えば、終了要求を送るコードは次のようになる。`node` の取得は省略している。

```csharp
WindowNodeRequestId request = node.Close();
// 戻り値は終了要求の受理。ウィンドウの終了完了ではない。
```

診断画面からも操作する場合は、既存の診断実行ポイントに接続し、同じスコープのシステムを明示登録する。

```csharp
services.AddDiagnosticExecutionPoint<BeforeWindowUpdate>("window.before-update");
services.AddWindowDiagnostics<BeforeWindowUpdate>();

// scope と windowSystem の生成は省略。以降は WindowSystem の所有スレッドで行う。
WindowDiagnosticRegistry registry = scope.ServiceProvider
    .GetRequiredService<WindowDiagnosticRegistry>();
using WindowDiagnosticRegistration registration = registry.Register("main", windowSystem);
```

`BeforeWindowUpdate` は利用側が定義する実行位置の型である。既存の診断接続と、その位置での `Pump` 呼び出しは別途必要になる。登録は対象のシステムより先に破棄する。

登録後は診断画面の Window ページで `main` と対象ウィンドウを選び、終了を要求できる。画面も要求 ID の受理だけを表示する。WindowSystem の更新後に「状態を更新」を実行して結果を確認する。要求の自動再送は行わない。

例の API は、基準コミットの `src/Platform/Lumyte.Windowing/WindowNode.cs` と `src/Diagnostics/Lumyte.Diagnostics.Windowing/WindowDiagnosticsExtensions.cs`、`WindowDiagnosticRegistry.cs` で確認する。

### 登録と操作の契約

登録 ID は利用者が決める。各ノードには登録期間だけ安定する `window-N` 形式の診断 ID を割り当て、Windowing の公開契約には追加しない。登録解除時にイベント購読と ID の対応を破棄する。

Observe 権限の操作はシステム一覧、ウィンドウ一覧、個別の状態を返す。個別の状態には親、提示 root、種別、役割、所有権、Native Window ID、論理・物理寸法、倍率、可視性、寿命を含める。Native オブジェクトや管理オブジェクトへの参照は通信へ渡さない。

Edit 権限の操作は `Show`、`Hide`、`Activate`、`BringToFront`、`Close` を呼び、受理した要求 ID を返す。借用ウィンドウの終了など現在の状態で拒否される操作は、`invalid-state` を返す。通信スレッドから直接操作せず、すべて既存の診断 Pump を通す。

診断画面は schema version 1 の完全な契約を確認し、一覧から個別状態へ段階的に取得する。操作後の予測状態を表示せず、利用者の明示更新で状態を読み直す。

### 公開 API

基準コミットに存在する登録 API の抜粋を示す。今回は説明の整理であり、宣言の追加・削除はない。診断結果の型と生成される操作は省略する。

```diff
 namespace Lumyte.Diagnostics.Windowing
 {
     public static class WindowDiagnosticsExtensions
     {
         // 既存の実行ポイントへ接続し、スコープ単位のレジストリを登録する。
         public static IServiceCollection AddWindowDiagnostics<TPoint>(this IServiceCollection services)
             where TPoint : class;
     }
     public sealed class WindowDiagnosticRegistry
     {
         // 同じスコープで一意な ID を与える。所有スレッドで呼ぶ。
         public WindowDiagnosticRegistration Register(string id, WindowSystem system);
     }
     public sealed class WindowDiagnosticRegistration : IDisposable
     {
         // 対応する登録だけを解除する。繰り返し呼び出せる。
         public void Dispose();
     }
 }
```

## 検討した代替案

当初の記録には候補間の比較が残っていないため、比較した案を後付けしない。採用理由として記録されているのは、明示登録と接続パッケージによって Windowing 本体の依存境界と ID 非公開方針を維持できることである。

## 結果と影響

利用者は Native と Hosted のウィンドウを同じ診断画面で観測・操作できる。一方、登録の寿命を管理し、操作の受理と完了を区別する必要がある。診断 ID は再登録後に変わり得る。

Edit 権限でウィンドウを閉じられるため、既存の認証・権限制御が必要である。毎フレームの取得、画像取得、入力イベント、Native ハンドルの公開は対象外とする。
