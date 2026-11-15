import fs from 'node:fs';
import path from 'node:path';

const base = path.resolve(process.argv[2] || '.');
const runNames = process.argv.includes('--validate-run') ? [process.argv[process.argv.indexOf('--validate-run') + 1]] : ['baseline-run1', 'candidate-run1', 'candidate-run2', 'baseline-run2'];
const methods = ['InterfaceSetDispatchComplete', 'ExplicitSnapshotSetDispatchComplete', 'MappedReferenceWrite', 'ReferenceWriteCopyComplete'];
const expected = new Set(methods.flatMap(method => [1, 64].map(count => method + '/' + count)));
const walk = dir => fs.readdirSync(dir, {withFileTypes: true}).flatMap(entry => entry.isDirectory() ? walk(path.join(dir, entry.name)) : [path.join(dir, entry.name)]);
const percentile = (values, p) => {
  const sorted = [...values].sort((a, b) => a - b);
  const index = (sorted.length - 1) * p;
  return sorted[Math.floor(index)] + (sorted[Math.ceil(index)] - sorted[Math.floor(index)]) * (index % 1);
};
const runs = {};
for (const name of runNames) {
  const cases = {};
  const files = walk(path.join(base, 'artifacts', name)).filter(file => file.endsWith('-report-full.json'));
  if (!files.length) throw new Error('Missing full JSON: ' + name);
  for (const file of files) {
    const document = JSON.parse(fs.readFileSync(file, 'utf8'));
    for (const bench of document.Benchmarks) {
      const count = Number(/Count:\s*(\d+)/.exec(bench.FullName)?.[1]);
      const key = bench.Method + '/' + count;
      if (!expected.has(key) || cases[key]) throw new Error('Unexpected/duplicate case: ' + name + '/' + key);
      const values = bench.Statistics?.OriginalValues;
      if (!values || values.length < 10 || values.some(value => !(value > 0))) throw new Error('Missing actual iterations: ' + name + '/' + key);
      const actual = bench.Measurements?.filter(value => value.IterationMode === 'Workload' && value.IterationStage === 'Actual') ?? [];
      if (actual.length !== values.length || actual.some(value => !(value.Operations > 0))) throw new Error('Invalid operation count: ' + key);
      const bytes = bench.Memory?.BytesAllocatedPerOperation;
      if (bytes === undefined || bytes === null) throw new Error('Missing MemoryDiagnoser: ' + key);
      cases[key] = {
        method: bench.Method, count, file: path.relative(base, file), fullName: bench.FullName,
        values, medianNs: percentile(values, 0.5), p95Ns: percentile(values, 0.95),
        bytesPerBatch: bytes, n: values.length, standardDeviationNs: bench.Statistics.StandardDeviation,
        actualMeasurementRows: actual.length,
      };
    }
  }
  if (Object.keys(cases).length !== expected.size) throw new Error('Incomplete run: ' + name);
  runs[name] = cases;
  console.log(name + ': validated ' + expected.size + ' cases');
}
if (process.argv.includes('--validate-run')) process.exit(0);
const comparison = [...expected].map(key => {
  const main = ['baseline-run1', 'baseline-run2'].map(name => runs[name][key]);
  const pr = ['candidate-run1', 'candidate-run2'].map(name => runs[name][key]);
  const mainMedian = percentile(main.flatMap(run => run.values), 0.5);
  const prMedian = percentile(pr.flatMap(run => run.values), 0.5);
  const spread = entries => Math.abs(entries[0].medianNs - entries[1].medianNs) / percentile(entries.map(run => run.medianNs), 0.5);
  const pairReductions = main.map((entry, index) => 100 * (1 - pr[index].medianNs / entry.medianNs));
  return {
    key, method: main[0].method, count: main[0].count,
    mainMedianNs: mainMedian, prMedianNs: prMedian,
    mainP95Ns: percentile(main.flatMap(run => run.values), 0.95), prP95Ns: percentile(pr.flatMap(run => run.values), 0.95),
    mainBytesPerBatch: main.map(run => run.bytesPerBatch), prBytesPerBatch: pr.map(run => run.bytesPerBatch),
    ratioMainOverPr: mainMedian / prMedian, percentTimeReduction: 100 * (1 - prMedian / mainMedian),
    absoluteReductionNs: mainMedian - prMedian,
    mainProcessMediansNs: main.map(run => run.medianNs), prProcessMediansNs: pr.map(run => run.medianNs),
    mainMedianSpread: spread(main), prMedianSpread: spread(pr), pairReductions,
    highDispersion: [...main, ...pr].some(run => run.standardDeviationNs / run.medianNs > 0.20),
    noisyOrInconclusive: spread(main) > 0.15 || spread(pr) > 0.15 || pairReductions[0] * pairReductions[1] < 0 || Math.abs(100 * (1 - prMedian / mainMedian)) < 5 || [...main, ...pr].some(run => run.standardDeviationNs / run.medianNs > 0.20),
  };
});
const result = {schema: 1, units: 'nanoseconds per batch; bytes allocated per batch; percentiles of iteration estimates', runs, comparison};
fs.writeFileSync(path.join(base, 'artifacts', 'summary.json'), JSON.stringify(result, null, 2) + '\n');
const header = 'method,count,main_median_ns,pr_median_ns,main_p95_ns,pr_p95_ns,main_bytes,pr_bytes,ratio,percent_reduction,noisy';
fs.writeFileSync(path.join(base, 'artifacts', 'summary.csv'), header + '\n' + comparison.map(row => [row.method,row.count,row.mainMedianNs,row.prMedianNs,row.mainP95Ns,row.prP95Ns,row.mainBytesPerBatch.join('|'),row.prBytesPerBatch.join('|'),row.ratioMainOverPr,row.percentTimeReduction,row.noisyOrInconclusive].join(',')).join('\n') + '\n');
console.log(JSON.stringify(comparison, null, 2));
