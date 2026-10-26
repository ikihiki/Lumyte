# DIAGNOSTICS-0005: WindowSystemの明示登録とscalar診断

- Status: Accepted
- Date: 2026-10-12

## Context

WindowSystemは複数のNative WindowとHosted領域の親子関係、可視性、配置、寿命、論理フォーカスを管理する。問題調査では管理ツリーと観測状態を確認し、必要に応じて表示・非表示・活性化・終了を要求したい。一方でWindowing本体をDiagnosticsや通信へ依存させず、内部管理ID、`IWindow`、HWNDなどのNative資源を遠隔通信へ公開してはならない。

## Decision

`Lumyte.Diagnostics.Windowing`を接続パッケージとして追加する。利用側は診断実行ポイントと同じスコープの`WindowDiagnosticRegistry`へ`WindowSystem`を明示登録する。登録IDは利用側が決め、各`WindowNode`には登録期間だけ安定する`window-N`診断IDを割り当てる。このIDをWindowingの公開契約へ追加しない。

Observe操作はSystem一覧、Window一覧、個別scalar snapshotを返す。snapshotには親、提示root、種別、役割、所有権、Native Window ID、論理・物理寸法、倍率、可視性、寿命を含める。Nativeオブジェクトや管理オブジェクトへの参照は保持しない。

Edit操作は`WindowNode`の`Show`、`Hide`、`Activate`、`BringToFront`、`Close`を呼び、受理された管理要求IDを返す。要求は非同期に完了するため、操作結果に予測状態を含めず、WindowSystemの更新後に`read`で観測する。借用Windowの終了など、現在の状態で拒否された操作は`invalid-state`として返す。

すべての操作は既存の診断Pumpを通じてWindowSystemの所有スレッドで実行する。通信スレッドからWindowSystemを直接操作しない。登録解除時はイベント購読と診断IDの対応を破棄する。

診断Serverにschema version 1の完全なWindow診断契約を検証する専用ページを追加する。ページはSystem一覧、Window一覧、選択したscalar snapshotを段階的に取得する。Edit操作は要求IDの受理だけを表示し、自動再送や完了状態の推測を行わない。利用者が状態更新を明示したときだけsnapshotを再取得する。

## Consequences

Windowing本体の依存境界とID非公開方針を維持したまま、NativeとHostedを同じ診断手順で観測・操作できる。診断IDは永続IDではなく、再登録後に変わり得る。Edit権限を持つ利用者はWindowを閉じられるため、診断Server側の既存の認証・権限制御を必須とする。

毎フレームpolling、画像取得、入力イベント、Native handleの公開はこの決定に含めない。
