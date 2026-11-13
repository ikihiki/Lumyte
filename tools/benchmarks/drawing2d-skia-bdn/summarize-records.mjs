import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const [root, destination] = process.argv.slice(2);
if (!root || !destination) throw new Error('Usage: node summarize-records.mjs <ABBA artifact root> <summary.json>');
function files(directory) {
    return fs.readdirSync(directory, {withFileTypes: true}).flatMap(entry => entry.isDirectory() ? files(path.join(directory, entry.name)) : [path.join(directory, entry.name)]);
}
const runs = [];
for (const name of ['A1', 'B1', 'B2', 'A2']) {
    const reports = files(path.join(root, name)).filter(file => file.endsWith('RecordFrameBenchmarks-report-full.json'));
    if (reports.length !== 1) throw new Error(name + ': expected one standard full JSON report');
    const bytes = fs.readFileSync(reports[0]);
    const report = JSON.parse(bytes);
    if (report.HostEnvironmentInfo.BenchmarkDotNetVersion !== '0.15.8' || report.Benchmarks.length !== 8) throw new Error(name + ': wrong runner or incomplete matrix');
    const cases = report.Benchmarks.map(benchmark => {
        const statistics = benchmark.Statistics;
        if (!statistics) throw new Error(name + ': benchmark did not produce statistics');
        const actual = benchmark.Measurements.filter(row => row.IterationMode === 'Workload' && row.IterationStage === 'Actual');
        const warmup = benchmark.Measurements.filter(row => row.IterationMode === 'Workload' && row.IterationStage === 'Warmup');
        const minimumIterationSeconds = Math.min(...actual.map(row => row.Nanoseconds / 1e9));
        const relativeConfidenceMargin = statistics.ConfidenceInterval.Margin / statistics.Mean;
        return {
            case: benchmark.Parameters.replace(/^Case=/, ''), displayInfo: benchmark.DisplayInfo,
            medianNs: statistics.Median, p95IterationEstimateNs: statistics.Percentiles.P95,
            allocatedBytesPerCompletedFrame: benchmark.Memory.BytesAllocatedPerOperation,
            actualIterations: statistics.N, rawActualIterations: actual.length,
            actualCompletedFrames: actual.reduce((sum, row) => sum + row.Operations, 0),
            measuredWorkloadSeconds: actual.reduce((sum, row) => sum + row.Nanoseconds / 1e9, 0),
            warmupIterations: warmup.length, minimumIterationSeconds,
            confidence999MarginNs: statistics.ConfidenceInterval.Margin,
            relativeConfidenceMargin, relativeConfidenceFullWidth: 2 * relativeConfidenceMargin, reachedDeclaredIterationCap: statistics.N >= 30,
            stabilityQualifiedAtTwoPercent: statistics.N >= 15 && 2 * relativeConfidenceMargin <= 0.02 && minimumIterationSeconds >= 0.1,
            allIterationEstimatesNs: statistics.OriginalValues,
            outlierCountRetained: statistics.AllOutliers.length,
        };
    });
    runs.push({name, variant: name[0], rawReportSha256: crypto.createHash('sha256').update(bytes).digest('hex'), host: report.HostEnvironmentInfo, cases});
}
const names = runs[0].cases.map(row => row.case);
const comparisons = names.map(name => {
    const row = Object.fromEntries(runs.map(run => [run.name, run.cases.find(item => item.case === name)]));
    if (Object.values(row).some(value => !value)) throw new Error('Missing paired case ' + name);
    return {
        case: name,
        processMediansNs: Object.fromEntries(Object.entries(row).map(([run, value]) => [run, value.medianNs])),
        processP95IterationEstimatesNs: Object.fromEntries(Object.entries(row).map(([run, value]) => [run, value.p95IterationEstimateNs])),
        processAllocatedBytes: Object.fromEntries(Object.entries(row).map(([run, value]) => [run, value.allocatedBytesPerCompletedFrame])),
        aThenBReductionPercent: 100 * (1 - row.B1.medianNs / row.A1.medianNs),
        bThenAReductionPercent: 100 * (1 - row.B2.medianNs / row.A2.medianNs),
        bothOrdersImproved: row.B1.medianNs < row.A1.medianNs && row.B2.medianNs < row.A2.medianNs,
        allFourProcessesStabilityQualified: Object.values(row).every(value => value.stabilityQualifiedAtTwoPercent),
    };
});
fs.writeFileSync(destination, JSON.stringify({schema: 1, order: ['A1', 'B1', 'B2', 'A2'], boundary: 'one recorded/prepared/submitted/GPU-completed/command-disposed/retired frame; serialized depth one; no presentation', percentiles: 'BDN normalized iteration estimates, not individual-frame latency percentiles', qualification: 'bounded 15–30 adaptive actual iterations; no outlier removal; 99.9% CI full width/mean <=2% (BDN half-width target 1%) and minimum measured iteration >=100ms; failures remain explicit', runs, comparisons}));
console.log(JSON.stringify(comparisons, null, 2));
