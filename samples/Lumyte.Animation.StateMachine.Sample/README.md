# アニメーション状態機械のサンプル

Composition の状態機械定義をコンストラクターへ直接渡し、内部制御とネストした Timeline を自動構築する。トリガーで有限 Repeat／Reverse のアクションを開始し、完了時のマーカーを収集して待機へ戻る。値の取得・適用は消費側で行う。

```sh
dotnet run --project samples/Lumyte.Animation.StateMachine.Sample
```
