# 全体Replayを通さない保持page直接描画の試作

製品APIではなく、前の保持＋平坦化試作の不足を調べるためのbenchmark-only実装です。
通常Canvas2D/Renderer2D、既存PR、既定backendの振る舞いは変更しません。

## 対応範囲

固定個数・固定描画順の単色FillRect、identity transform、clipなし、1024×1024 Rgba8Srgb targetです。
色/矩形の置換は可能ですが、insert/remove/reorder、任意clip、path/text、layer/effect、device lossからの復旧は今回の対象外です。
未対応の入力を別rendererへ黙ってfallbackせず、明示的に拒否します。

## コピーと再利用の境界

- CPUの描画順を64矩形のimmutable pageに分割します。変更commandだけを既存rectangle shader ABIの6 Vector4へpackし、変更pageとroot参照配列だけをCOWします。
- 1万command全体へのReplay、End、型別配列の再構築はdirect経路にありません。
- CPU page粒度とGPU draw粒度を分離し、GPUは従来同様最大1024矩形のrunを使います。論理page数をそのままdraw数へ変換しません。
- CPU pageのCOWコピー、frame wrapper、page参照の比較、必要なCPU→GPU転送は残ります。ゼロコピー全般やframe全体のO(dirty)を主張しません。
- 全変更では古いpageをcloneせず新pageをまとめて構築します。これは全変更専用のbulk経路で、閾値の最適化は未実施です。

## GPU resident slot

各frame slotがupload/data bufferとargument tableを独立所有します。保持しているCPU page参照と今回のsnapshotを比較し、そのslotに未反映の全pageを追いつかせます。「直近frameの差分だけ」を適用しません。

準備では変更chunkだけをmap/copyします。静止ならpayload pack/uploadは0を期待します。1 frame in flightの単一変更でも最小転送は1 chunkであり、64矩形なら最大6,144 byteです。複数slotでは、そのslotが見ていない更新範囲の転送が累積します。最終端の短いchunkでは少なくなります。
GPU実行は保存画像の部分描画ではなく、毎frameの全target clearと全drawです。改善対象は再記録・pack・upload準備で、dirty rectangleではありません。

## lifetime

1. slot.PrepareAsyncでfreshなframe ownerを作り、そのslotをbusyにします。
2. Frame.Recordは一回だけ。描画順を保ち、GPU使用中slotのbuffer/tableを書き換えません。
3. Queue.Submitが実際に返したsubmissionをFrame.Submittedへ渡します。
4. GPU完了を確認してFrame.Completeすると初めてresident page参照/初期化flagを公開します。
5. submission、command、frame、slot、rendererの順序を守って返却します。

Recordしただけではupload完了と見なしません。準備取消/未送信command破棄でresident版を進めません。
未完了/失敗/成否不明のsubmissionを安全に解放できたと仮定せず、slotを再利用しない扱いにします。
基本Graphics APIはsubmissionとcommandの所属関係を完全に検査できないため、返された正確なsubmissionを渡す責任は計測側にあります。

## 検証と測定

- CPU入力分離、旧snapshot保持、64/1024境界、拒否時の原子性。
- renderer入力へ直接pageを渡すこと、静止pack/upload=0、差分byte数、draw数、GPU allocation数。
- slot busy、二重Record、未送信破棄、GPU完了前のresident公開拒否。
- 3 slot間で異なる更新が累積しても取りこぼさないこと。
- alpha重なりを含む画素一致、old/new snapshotの交互使用。
- main/#105 legacy、旧保持＋平坦化、directを同じ変更入力で比較。

測定はsource固定・全build/check完了後に直列実行します。GPU timestampとCPU壁時計を分け、1/3 frame in flightは最終drain込みのheadless throughputとして扱います。Present/VSyncや一般UIの表示FPSではありません。
