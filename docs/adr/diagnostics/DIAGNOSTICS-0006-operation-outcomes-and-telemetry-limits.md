# ADR-DIAGNOSTICS-0006: 診断結果の型付き参照とテレメトリー上限の共有

- 状態: 採用
- 日付: 2026-10-26

## 背景

ログ収集は利用者の32フィールドに例外情報を含む5フィールドを追加できるが、受信側は36フィールドまでしか許可していなかった。また操作結果の閉じた状態集合を文字列比較で扱う必要がある。

## 決定

収集と両通信方式の受信で同じ上限を参照する。37フィールドの正当なログを受信できるようにする。旧サーバーはこのログを拒否するため、修正は受信側にも適用する。

結果の既存の文字列プロパティとwire形式を保持し、型付き状態を返すメソッドを追加する。メソッドはJSONの追加プロパティにならず、既存の通信表現を変えない。未知の文字列はUnknownとして判定でき、元の値はStatusに残る。

比較元はmainの16d856a。

```diff
 namespace Lumyte.Diagnostics
 {
+    public static class DiagnosticTelemetryLimits
+    {
+        public const int MaximumUserFields = 32;
+        public const int MaximumEventFields = MaximumUserFields + 5;
+    }
+    public enum DiagnosticOperationStatus { Unknown, Success, Rejected, Conflict }
     public sealed record DiagnosticOperationResult
     {
+        public DiagnosticOperationStatus GetStatus();
     }
 }
```

HTTPの接続・コマンド受信・送信で、応答JSONの復号失敗を共通のDiagnosticTransportExceptionに包む。呼び出し側のキャンセルはOperationCanceledExceptionのまま伝える。これは既存の通信例外契約の具体化である。

## 検討した代替案

利用者フィールドを31件に減らす方法は既存の収集契約を縮小するため採用しない。Statusの型を直接enumに変える方法は利用コードとwire形式に影響するため採用しない。

## 結果と影響

上限の更新漏れと文字列比較を減らす。既存コンストラクター・プロパティ・エンコーダーは維持する。上限を超えるログの拒否、収集した例外ログの両方式での往復、HTTP復号失敗、既存wire表現を回帰テストで確認する。

本ADRは[診断通信](DIAGNOSTICS-0001-diagnostics-transport.md)の契約を補足する。
