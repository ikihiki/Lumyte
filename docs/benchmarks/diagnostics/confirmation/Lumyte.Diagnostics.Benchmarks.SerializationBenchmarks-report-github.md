# SerializationBenchmarks — BenchmarkDotNet 出力

```text

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
AMD EPYC 9V74 2.60GHz, 1 CPU, 5 logical and 5 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=5  IterationTime=150ms
LaunchCount=1  WarmupCount=3

```

| Method             | Categories  | Workload      | Mean               | Error             | StdDev           | Ratio    | RatioSD   | Gen0        | Gen1        | Gen2        | Allocated    | Alloc Ratio   |
| ------------------ | ----------- | ------------- | -----------------: | ----------------: | ---------------: | -------: | --------: | ----------: | ----------: | ----------: | -----------: | ------------: |
| **JsonDecode**     | **Decode**  | **Operation** | **2,251.4 ns**     | **833.54 ns**     | **216.47 ns**    | **1.01** | **0.12**  | **0.1445**  | **-**       | **-**       | **2512 B**   | **1.00**      |
| MessagePackDecode  | Decode      | Operation     | 614.5 ns           | 54.59 ns          | 8.45 ns          | 0.27     | 0.02      | 0.0463      | -           | -           | 784 B        | 0.31          |
| MemoryPackDecode   | Decode      | Operation     | 182.4 ns           | 10.31 ns          | 2.68 ns          | 0.08     | 0.01      | 0.0448      | -           | -           | 760 B        | 0.30          |
| ProtobufDecode     | Decode      | Operation     | 846.1 ns           | 167.98 ns         | 43.63 ns         | 0.38     | 0.04      | 0.0427      | -           | -           | 752 B        | 0.30          |
| **JsonDecode**     | **Decode**  | **Metrics**   | **266,644.2 ns**   | **131,000.91 ns** | **34,020.52 ns** | **1.02** | **0.18**  | **5.2083**  | **-**       | **-**       | **125664 B** | **1.00**      |
| MessagePackDecode  | Decode      | Metrics       | 65,090.3 ns        | 5,628.86 ns       | 1,461.80 ns      | 0.25     | 0.03      | 5.1306      | 0.9328      | -           | 87232 B      | 0.69          |
| MemoryPackDecode   | Decode      | Metrics       | 24,009.7 ns        | 6,239.46 ns       | 1,620.37 ns      | 0.09     | 0.01      | 5.1414      | 0.9640      | -           | 87208 B      | 0.69          |
| ProtobufDecode     | Decode      | Metrics       | 73,345.2 ns        | 7,878.46 ns       | 2,046.01 ns      | 0.28     | 0.04      | 3.1780      | -           | -           | 68864 B      | 0.55          |
| **JsonDecode**     | **Decode**  | **Trace**     | **264,286.9 ns**   | **398,427.78 ns** | **61,657.14 ns** | **1.03** | **0.28**  | **4.8077**  | **-**       | **-**       | **151264 B** | **1.00**      |
| MessagePackDecode  | Decode      | Trace         | 73,497.2 ns        | 1,576.73 ns       | 244.00 ns        | 0.29     | 0.05      | 6.6057      | 1.5244      | -           | 111808 B     | 0.74          |
| MemoryPackDecode   | Decode      | Trace         | 27,990.3 ns        | 1,742.67 ns       | 269.68 ns        | 0.11     | 0.02      | 6.6451      | 1.7960      | -           | 111784 B     | 0.74          |
| ProtobufDecode     | Decode      | Trace         | 85,739.6 ns        | 6,870.02 ns       | 1,784.12 ns      | 0.34     | 0.06      | 5.1606      | 0.5734      | -           | 93440 B      | 0.62          |
| **JsonDecode**     | **Decode**  | **Log**       | **352,501.3 ns**   | **373,604.67 ns** | **97,023.95 ns** | **1.06** | **0.38**  | **6.2500**  | **-**       | **-**       | **155360 B** | **1.00**      |
| MessagePackDecode  | Decode      | Log           | 88,970.2 ns        | 12,419.74 ns      | 3,225.37 ns      | 0.27     | 0.07      | 6.6106      | 1.2019      | -           | 116928 B     | 0.75          |
| MemoryPackDecode   | Decode      | Log           | 38,653.2 ns        | 14,059.78 ns      | 3,651.28 ns      | 0.12     | 0.03      | 6.8182      | 1.5909      | -           | 116904 B     | 0.75          |
| ProtobufDecode     | Decode      | Log           | 95,171.8 ns        | 10,828.70 ns      | 2,812.18 ns      | 0.29     | 0.07      | 5.8594      | 1.3021      | -           | 98560 B      | 0.63          |
| **JsonDecode**     | **Decode**  | **Graph**     | **1,973,947.7 ns** | **305,063.14 ns** | **79,223.93 ns** | **1.00** | **0.05**  | **31.2500** | **-**       | **-**       | **962216 B** | **1.00**      |
| MessagePackDecode  | Decode      | Graph         | 737,431.1 ns       | 121,607.41 ns     | 31,581.06 ns     | 0.37     | 0.02      | 20.8333     | -           | -           | 672192 B     | 0.70          |
| MemoryPackDecode   | Decode      | Graph         | 212,250.1 ns       | 88,400.78 ns      | 22,957.40 ns     | 0.11     | 0.01      | 38.4615     | 14.4231     | -           | 672168 B     | 0.70          |
| ProtobufDecode     | Decode      | Graph         | 617,706.7 ns       | 24,001.82 ns      | 6,233.20 ns      | 0.31     | 0.01      | 29.1667     | 12.5000     | -           | 528256 B     | 0.55          |
| **JsonDecode**     | **Decode**  | **Image**     | **61,384.6 ns**    | **8,145.34 ns**   | **1,260.50 ns**  | **1.00** | **0.03**  | **61.6438** | **61.6438** | **61.6438** | **263305 B** | **1.00**      |
| MessagePackDecode  | Decode      | Image         | 33,142.7 ns        | 4,917.14 ns       | 1,276.97 ns      | 0.54     | 0.02      | 50.4237     | 50.4237     | 50.4237     | 262700 B     | 1.00          |
| MemoryPackDecode   | Decode      | Image         | 23,677.9 ns        | 2,209.77 ns       | 341.96 ns        | 0.39     | 0.01      | 50.8178     | 50.8178     | 50.8178     | 262690 B     | 1.00          |
| ProtobufDecode     | Decode      | Image         | 28,845.2 ns        | 7,023.38 ns       | 1,086.88 ns      | 0.47     | 0.02      | 52.9891     | 52.9891     | 52.9891     | 262778 B     | 1.00          |
| **JsonEncode**     | **Encode**  | **Operation** | **1,158.3 ns**     | **48.06 ns**      | **12.48 ns**     | **1.00** | **0.01**  | **0.0867**  | **-**       | **-**       | **1520 B**   | **1.00**      |
| MessagePackEncode  | Encode      | Operation     | 453.7 ns           | 41.13 ns          | 10.68 ns         | 0.39     | 0.01      | 0.0117      | -           | -           | 200 B        | 0.13          |
| MemoryPackEncode   | Encode      | Operation     | 128.9 ns           | 12.02 ns          | 1.86 ns          | 0.11     | 0.00      | 0.0164      | -           | -           | 280 B        | 0.18          |
| ProtobufEncode     | Encode      | Operation     | 634.5 ns           | 110.51 ns         | 28.70 ns         | 0.55     | 0.02      | 0.0318      | -           | -           | 536 B        | 0.35          |
| **JsonEncode**     | **Encode**  | **Metrics**   | **132,249.3 ns**   | **61,070.49 ns**  | **15,859.81 ns** | **1.01** | **0.15**  | **2.3148**  | **-**       | **-**       | **71656 B**  | **1.00**      |
| MessagePackEncode  | Encode      | Metrics       | 43,853.7 ns        | 3,835.45 ns       | 593.54 ns        | 0.34     | 0.04      | -           | -           | -           | 15312 B      | 0.21          |
| MemoryPackEncode   | Encode      | Metrics       | 10,551.5 ns        | 229.04 ns         | 59.48 ns         | 0.08     | 0.01      | 1.4501      | -           | -           | 24792 B      | 0.35          |
| ProtobufEncode     | Encode      | Metrics       | 55,230.5 ns        | 11,123.89 ns      | 2,888.84 ns      | 0.42     | 0.05      | 2.5144      | -           | -           | 46776 B      | 0.65          |
| **JsonEncode**     | **Encode**  | **Trace**     | **171,444.1 ns**   | **142,180.90 ns** | **36,923.93 ns** | **1.04** | **0.30**  | **2.5000**  | **-**       | **-**       | **83432 B**  | **1.00**      |
| MessagePackEncode  | Encode      | Trace         | 48,748.2 ns        | 1,482.94 ns       | 385.12 ns        | 0.30     | 0.06      | 1.3021      | -           | -           | 23632 B      | 0.28          |
| MemoryPackEncode   | Encode      | Trace         | 14,450.3 ns        | 2,165.23 ns       | 562.30 ns        | 0.09     | 0.02      | 2.0218      | -           | -           | 34264 B      | 0.41          |
| ProtobufEncode     | Encode      | Trace         | 69,893.5 ns        | 1,386.08 ns       | 214.50 ns        | 0.42     | 0.09      | 5.0926      | 0.4630      | -           | 90800 B      | 1.09          |
| **JsonEncode**     | **Encode**  | **Log**       | **142,037.7 ns**   | **19,051.67 ns**  | **4,947.66 ns**  | **1.00** | **0.05**  | **4.5956**  | **-**       | **-**       | **85344 B**  | **1.00**      |
| MessagePackEncode  | Encode      | Log           | 57,268.7 ns        | 1,036.03 ns       | 269.05 ns        | 0.40     | 0.01      | 1.5432      | -           | -           | 27488 B      | 0.32          |
| MemoryPackEncode   | Encode      | Log           | 15,098.6 ns        | 636.03 ns         | 98.43 ns         | 0.11     | 0.00      | 2.2765      | -           | -           | 38248 B      | 0.45          |
| ProtobufEncode     | Encode      | Log           | 74,433.4 ns        | 9,395.65 ns       | 2,440.02 ns      | 0.52     | 0.02      | 5.2083      | -           | -           | 100056 B     | 1.17          |
| **JsonEncode**     | **Encode**  | **Graph**     | **1,228,414.7 ns** | **351,299.05 ns** | **91,231.25 ns** | **1.00** | **0.09**  | **93.7500** | **93.7500** | **93.7500** | **551515 B** | **1.00**      |
| MessagePackEncode  | Encode      | Graph         | 433,761.1 ns       | 45,237.64 ns      | 11,748.07 ns     | 0.35     | 0.02      | 31.2500     | 31.2500     | 31.2500     | 117131 B     | 0.21          |
| MemoryPackEncode   | Encode      | Graph         | 117,853.8 ns       | 5,660.28 ns       | 1,469.96 ns      | 0.10     | 0.01      | 46.6772     | 46.6772     | 46.6772     | 191163 B     | 0.35          |
| ProtobufEncode     | Encode      | Graph         | 460,428.0 ns       | 65,825.69 ns      | 10,186.60 ns     | 0.38     | 0.03      | 53.5714     | 53.5714     | 53.5714     | 370502 B     | 0.67          |
| **JsonEncode**     | **Encode**  | **Image**     | **51,437.6 ns**    | **6,888.59 ns**   | **1,788.94 ns**  | **1.00** | **0.05**  | **67.7374** | **67.7374** | **67.7374** | **350439 B** | **1.00**      |
| MessagePackEncode  | Encode      | Image         | 60,454.1 ns        | 24,809.96 ns      | 6,443.07 ns      | 1.18     | 0.12      | 103.9013    | 103.9013    | 103.9013    | 524490 B     | 1.50          |
| MemoryPackEncode   | Encode      | Image         | 30,125.4 ns        | 2,752.93 ns       | 714.93 ns        | 0.59     | 0.02      | 55.0743     | 55.0743     | 55.0743     | 262280 B     | 0.75          |
| ProtobufEncode     | Encode      | Image         | 57,703.9 ns        | 3,584.95 ns       | 554.77 ns        | 1.12     | 0.04      | 105.6743    | 105.6743    | 105.6743    | 525598 B     | 1.50          |
