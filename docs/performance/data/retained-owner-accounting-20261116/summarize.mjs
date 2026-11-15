import fs from 'node:fs';
import path from 'node:path';
const root = process.argv[2];
const rows = [];
for (const mode of ['frames', 'reads']) {
  for (const run of ['A1', 'B1', 'B2', 'A2']) {
    const name = mode === 'frames' ? 'OwnerAccountingFrameBenchmarks' : 'OwnerAccountingStatisticsBenchmarks';
    const file = path.join(root, mode, run, 'results', name + '-report-full.json');
    const data = JSON.parse(fs.readFileSync(file));
    if (data.Benchmarks.length !== (mode === 'frames' ? 8 : 2)) throw new Error('Incomplete case set: ' + file);
    for (const b of data.Benchmarks) {
      if (!b.Statistics || !b.Memory || b.Statistics.N < 15 || b.Statistics.N > 30) throw new Error('Missing/capped results: ' + file);
      rows.push({mode, run, parameters: b.Parameters, medianNs: b.Statistics.Median,
        p95Ns: b.Statistics.Percentiles.P95, meanNs: b.Statistics.Mean, stdDevNs: b.Statistics.StandardDeviation,
        n: b.Statistics.N, ci99_9HalfWidthNs: b.Statistics.ConfidenceInterval.Margin,
        relativeCi99_9: b.Statistics.ConfidenceInterval.Margin / b.Statistics.Mean,
        qualifiedOnePercent: b.Statistics.ConfidenceInterval.Margin <= b.Statistics.Mean * 0.01,
        allocatedBytes: b.Memory.BytesAllocatedPerOperation});
    }
  }
}
const comparisons = [];
for (const key of new Set(rows.map(r => r.mode + '|' + r.parameters))) {
  const group = rows.filter(r => r.mode + '|' + r.parameters === key);
  const byRun = Object.fromEntries(group.map(r => [r.run, r]));
  const avg = (a, b, field) => (byRun[a][field] + byRun[b][field]) / 2;
  const baselineMedianNs = avg('A1', 'A2', 'medianNs');
  const candidateMedianNs = avg('B1', 'B2', 'medianNs');
  comparisons.push({key, baselineMedianNs, candidateMedianNs,
    medianChangePercent: (candidateMedianNs / baselineMedianNs - 1) * 100,
    baselineP95Ns: avg('A1', 'A2', 'p95Ns'), candidateP95Ns: avg('B1', 'B2', 'p95Ns'),
    baselineAllocatedBytes: avg('A1', 'A2', 'allocatedBytes'), candidateAllocatedBytes: avg('B1', 'B2', 'allocatedBytes'),
    ABChangePercent: (byRun.B1.medianNs / byRun.A1.medianNs - 1) * 100,
    BAChangePercent: (byRun.B2.medianNs / byRun.A2.medianNs - 1) * 100,
    allProcessesMeetOnePercent: group.every(r => r.qualifiedOnePercent)});
}
console.log(JSON.stringify({units: 'nanoseconds; bytes/op', percentiles: 'BDN iteration estimates, not individual frame latencies', rows, comparisons}, null, 2));
