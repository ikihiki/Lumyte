# OpenAL オーディオサンプル

OpenAL Soft をインストールし、`mise exec -- dotnet run --project samples/Lumyte.Audio.Sample -c Release` で実行します。生成 PCM の再生と共通契約の検証を行い、成功時に `OpenAL audio contracts passed.` と出力します。

ヘッドレスの Linux では `ALSOFT_DRIVERS=null` を指定できます。null driver も OpenAL の実ライブラリと再生時計を使用しますが、スピーカーへ出力しません。

複数 device の分離、引数検証、voice 上限、参照中の解放拒否、再生・一時停止・再開・停止・ループ・自然終了、所有スレッド以外からの操作拒否を確認します。PCM の取得・デコードは行いません。
