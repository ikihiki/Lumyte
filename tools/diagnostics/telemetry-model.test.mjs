import assert from 'node:assert/strict';
import test from 'node:test';
import { filterTelemetry, formatDuration, groupTraces, metricSeries, normalizeSamples, scalarText, TelemetryViewState } from '../../src/Diagnostics/Lumyte.Diagnostics.Server/Web/telemetry-model.mjs';

const integer = value => ({ kind: 1, int64: String(value) });
const text = value => ({ kind: 3, string: value });
const event = (kind, name, fields = {}) => ({ kind, name, timestamp: '1', value: text(name), fields, traceId: 'trace-a', spanId: 'span-a', parentSpanId: '0000000000000000', durationTicks: '10000' });

test('structured logs combine severity, trace correlation and field search', () => {
    const info = event('log', 'Category', { 'log.level': text('Information'), 'request.id': integer(42) });
    const error = event('log', 'Category', { 'log.level': text('Error'), 'request.id': integer(43) });
    const unknown = event('log', 'Category');
    const other = { ...error, traceId: 'trace-b' };
    assert.deepEqual(filterTelemetry([info, error, other, unknown], { kind: 'log', minimumLevel: 'Warning', traceId: 'trace-a', query: '43' }), [error]);
    assert.equal(filterTelemetry([unknown], { kind: 'log' }).length, 1);
    assert.equal(filterTelemetry([unknown], { kind: 'log', minimumLevel: 'Error' }).length, 0);
});

test('pause holds the displayed snapshot while bounded latest data advances', () => {
    const state = new TelemetryViewState();
    const first = [event('log', 'first')];
    const next = [event('log', 'next')];
    state.update(first); state.setPaused(true); state.update(next);
    assert.equal(state.displayed, first);
    assert.equal(state.latest, next);
    state.setPaused(false);
    assert.equal(state.displayed, next);
    state.reset();
    assert.deepEqual(state.displayed, []);
    assert.deepEqual(state.latest, []);
    assert.equal(state.paused, false);
});

test('Trace rows follow parents even when children arrive first', () => {
    const root = { ...event('span', 'root'), spanId: 'root', durationTicks: '50000' };
    const child = { ...event('span', 'child'), spanId: 'child', parentSpanId: 'root', value: text('Error') };
    const [trace] = groupTraces([child, root]);
    assert.equal(trace.name, 'root');
    assert.equal(trace.hasError, true);
    assert.equal(trace.longest, 50000n);
    assert.deepEqual(trace.spans.map(row => [row.span.name, row.depth]), [['root', 0], ['child', 1]]);
});

test('partial and cyclic traces remain bounded and expose missing parents', () => {
    const orphan = { ...event('span', 'orphan'), parentSpanId: 'missing' };
    const cycle = [{ ...event('span', 'a'), spanId: 'a', parentSpanId: 'b' }, { ...event('span', 'b'), spanId: 'b', parentSpanId: 'a' }];
    assert.equal(groupTraces([orphan])[0].spans[0].parentMissing, true);
    const [group] = groupTraces(cycle);
    assert.equal(group.spans.length, 2);
    assert.ok(group.spans.every(row => row.depth <= 32));
    assert.deepEqual(groupTraces([event('log', 'log')]), []);
});

test('metric series distinguish tag values and canonicalize tag order', () => {
    const first = { ...event('metric', 'frames', { a: text('one'), b: integer(2) }), value: integer(42) };
    const next = { ...first, value: integer(43), fields: { b: integer(2), a: text('one') } };
    const different = { ...first, fields: { a: text('two'), b: integer(2) } };
    const groups = metricSeries([first, next, different]);
    assert.equal(groups.length, 2);
    assert.equal(groups[0].samples.length, 2);
    assert.equal(scalarText(groups[0].latest.value), '43');
    assert.equal(groups[1].samples.length, 1);
});

test('Int64 chart normalization preserves adjacent values above Number precision', () => {
    const values = ['9223372036854775805', '9223372036854775806', '9223372036854775807'].map(integer);
    assert.deepEqual(normalizeSamples(values), [0, 0.5, 1]);
    assert.equal(scalarText(values[2]), '9223372036854775807');
    assert.deepEqual(normalizeSamples([integer('-9223372036854775808'), integer('9223372036854775807')]), [0, 1]);
});

test('duration formatting and finite double charts avoid overflow', () => {
    assert.equal(formatDuration('10001'), '1.0001 ms');
    assert.equal(formatDuration('9223372036854775807'), '922337203685477.5807 ms');
    assert.deepEqual(normalizeSamples([-Number.MAX_VALUE, 0, Number.MAX_VALUE].map(value => ({ kind: 2, double: value }))), [0, 0.5, 1]);
    assert.deepEqual(normalizeSamples([integer(42), integer(42)]), [0.5, 0.5]);
    assert.deepEqual(normalizeSamples([{ kind: 2, double: Infinity }]), []);
});
