// Presentation queries over detached, bounded diagnostic snapshots.
export const logLevels = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'];

export function scalarText(value) {
    if (!value) return '';
    return [() => String(value.boolean), () => value.int64, () => String(value.double), () => value.string][value.kind]?.() ?? '?';
}

export function filterTelemetry(events, { kind, query = '', traceId = '', minimumLevel = '' } = {}) {
    const search = query.toLocaleLowerCase();
    const threshold = logLevels.indexOf(minimumLevel);
    return events.filter(item => {
        if (kind && item.kind !== kind) return false;
        if (traceId && item.traceId !== traceId) return false;
        if (kind === 'log' && threshold >= 0 && logLevels.indexOf(scalarText(item.fields['log.level'])) < threshold) return false;
        return !search || JSON.stringify(item).toLocaleLowerCase().includes(search);
    });
}

export function formatDuration(ticks) {
    const value = BigInt(ticks);
    return `${value / 10000n}.${String(value % 10000n).padStart(4, '0').replace(/0+$/, '') || '0'} ms`;
}

export function groupTraces(events) {
    const traces = new Map();
    for (const span of events.filter(item => item.kind === 'span' && item.traceId)) {
        if (!traces.has(span.traceId)) traces.set(span.traceId, []);
        traces.get(span.traceId).push(span);
    }
    return Array.from(traces, ([traceId, spans]) => {
        const byId = new Map(spans.map(span => [span.spanId, span]));
        const depthOf = span => {
            const visited = new Set([span.spanId]);
            let current = span;
            let depth = 0;
            while (depth < 32 && byId.has(current.parentSpanId) && !visited.has(current.parentSpanId)) {
                visited.add(current.parentSpanId);
                current = byId.get(current.parentSpanId);
                depth++;
            }
            return depth;
        };
        const roots = spans.filter(span => !byId.has(span.parentSpanId));
        const latest = spans.reduce((value, span) => BigInt(span.timestamp) > value ? BigInt(span.timestamp) : value, 0n);
        const longest = spans.reduce((value, span) => BigInt(span.durationTicks) > value ? BigInt(span.durationTicks) : value, 0n);
        const rows = spans.map(span => ({ span, depth: depthOf(span), parentMissing: Boolean(span.parentSpanId && span.parentSpanId !== '0000000000000000' && !byId.has(span.parentSpanId)) }));
        const ordered = [];
        const visited = new Set();
        const visit = row => {
            if (visited.has(row)) return;
            visited.add(row); ordered.push(row);
            for (const child of rows.filter(value => value.span.parentSpanId === row.span.spanId)) visit(child);
        };
        for (const row of rows.filter(value => !byId.has(value.span.parentSpanId))) visit(row);
        for (const row of rows) visit(row);
        return { traceId, name: (roots[0] ?? spans[0]).name, latest, longest, hasError: spans.some(span => scalarText(span.value) === 'Error'), spans: ordered };
    }).sort((a, b) => a.latest === b.latest ? 0 : a.latest > b.latest ? -1 : 1);
}

export function metricSeries(events) {
    const series = new Map();
    for (const item of events.filter(event => event.kind === 'metric')) {
        const tags = Object.keys(item.fields).sort().map(key => [key, item.fields[key]]);
        const key = JSON.stringify([item.name, item.value.kind, tags]);
        if (!series.has(key)) series.set(key, { name: item.name, tags, samples: [], latest: item });
        const group = series.get(key);
        group.samples.push(item.value);
        group.latest = item;
    }
    return Array.from(series.values());
}

export function normalizeSamples(samples) {
    if (!samples.length) return [];
    if (samples.every(value => value.kind === 1)) {
        const values = samples.map(value => BigInt(value.int64));
        const minimum = values.reduce((a, b) => a < b ? a : b);
        const maximum = values.reduce((a, b) => a > b ? a : b);
        const range = maximum - minimum;
        return values.map(value => range === 0n ? 0.5 : Number((value - minimum) * 10000n / range) / 10000);
    }
    if (!samples.every(value => value.kind === 2 && Number.isFinite(value.double))) return [];
    const values = samples.map(value => value.double);
    const scale = Math.max(1, ...values.map(Math.abs));
    const scaled = values.map(value => value / scale);
    const minimum = Math.min(...scaled);
    const maximum = Math.max(...scaled);
    return scaled.map(value => minimum === maximum ? 0.5 : (value - minimum) / (maximum - minimum));
}

export class TelemetryViewState {
    latest = [];
    displayed = [];
    paused = false;

    update(events) {
        this.latest = events;
        if (!this.paused) this.displayed = events;
    }

    setPaused(paused) {
        this.paused = paused;
        if (!paused) this.displayed = this.latest;
    }

    reset() {
        this.latest = [];
        this.displayed = [];
        this.paused = false;
    }
}
