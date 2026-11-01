import test from 'node:test';
import assert from 'node:assert/strict';
import { analyzeRun, compareRuns, toCSV } from '../src/core/analytics.mjs';

const head = (id, lane, targetUs, deltaUs, grade = 'PERFECT', pattern = 'single') => ({ noteId: id, lane, targetUs, timeUs: targetUs + (deltaUs ?? 120_001), deltaUs, grade, kind: 'head', pattern });
const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-9, `${actual} != ${expected}`);

test('timing uses actual head hits and excludes MISS and hold sustain', () => {
  const judgments = [head('a', 0, 1_000_000, -10_000), head('b', 1, 2_000_000, 0), head('c', 1, 3_000_000, 20_000), head('d', 1, 4_000_000, null, 'MISS'), { ...head('a', 0, 5_000_000, 0), kind: 'hold' }];
  const result = analyzeRun({ judgments }, { durationUs: 20_000_000, notes: [{ id: 'a', timeUs: 1_000_000, endUs: 5_000_000 }] });
  assert.equal(result.timing.count, 3);
  near(result.timing.meanMs, 10 / 3);
  assert.equal(result.timing.medianMs, 0);
  assert.equal(result.timing.maeMs, 10);
  near(result.timing.stdMs, Math.sqrt(1400 / 9));
  assert.equal(result.timing.madMs, 10);
  assert.equal(result.timing.p95Ms, 20);
  assert.deepEqual([result.timing.early, result.timing.late, result.timing.exact], [1, 1, 1]);
  assert.equal(result.headCount, 4);
  assert.equal(result.missCount, 1);
  assert.equal(result.missRate, 25);
  assert.equal(result.lanes[1].count, 3);
  assert.equal(result.lanes[1].timingCount, 2);
  near(result.lanes[1].missRate, 100 / 3);
  assert.equal(result.patterns[0].count, 4);
  assert.deepEqual(result.holds, { total: 1, evaluated: 1, broken: 0, successRate: 100 });
});

test('empty and entirely missed runs have no fabricated zero-ms timing', () => {
  const empty = analyzeRun();
  assert.equal(empty.timing.count, 0);
  for (const field of ['meanMs', 'medianMs', 'maeMs', 'stdMs', 'madMs', 'p95Ms']) assert.equal(empty.timing[field], null);
  assert.equal(empty.holds.successRate, null);
  assert.equal(empty.chords.medianSpreadMs, null);
  assert.deepEqual(empty.recommendations, []);
  const missed = analyzeRun({ judgments: [head('miss', 0, 1_000_000, null, 'MISS')] }, { durationUs: 2_000_000 });
  assert.equal(missed.missRate, 100);
  assert.equal(missed.timing.meanMs, null);
  assert.equal(missed.histogram.reduce((sum, bin) => sum + bin.count, 0), 0);
});

test('five-ms bins preserve negative, exact-boundary and maximum-window hits', () => {
  const values = [-120_000, -5_000, -1, 0, 4_999, 5_000, 120_000];
  const analysis = analyzeRun({ judgments: values.map((delta, index) => head(String(index), 0, index * 1_000_000, delta)) });
  assert.ok(analysis.histogram.every(bin => bin.toMs - bin.fromMs === 5));
  assert.equal(analysis.histogram.reduce((sum, bin) => sum + bin.count, 0), values.length);
  assert.equal(analysis.histogram[0].count, 1);
  assert.equal(analysis.histogram.at(-1).count, 1);
  assert.equal(analysis.histogram.find(bin => bin.fromMs === -5).count, 2);
  assert.equal(analysis.histogram.find(bin => bin.fromMs === 0).count, 2);
  assert.equal(analysis.histogram.find(bin => bin.fromMs === 5).count, 1);
  const expanded = analyzeRun({ judgments: [head('wide', 0, 0, 240_000)] });
  assert.equal(expanded.histogram.at(-1).toMs, 240);
  assert.equal(expanded.histogram.at(-1).count, 1);
});

test('segments use original target time and weighted judged units without filling unplayed areas', () => {
  const judgments = [head('a', 0, 9_999_999, 40_000, 'GREAT'), head('b', 1, 10_000_000, 0), head('c', 2, 10_500_000, null, 'MISS'), { ...head('b', 1, 11_000_000, 0), kind: 'hold' }];
  const analysis = analyzeRun({ judgments }, { durationUs: 30_000_000 });
  assert.deepEqual(analysis.segments.map(segment => segment.count), [1, 3, 0]);
  assert.equal(analysis.segments[0].accuracy, 90);
  near(analysis.segments[1].accuracy, 200 / 3);
  assert.equal(analysis.segments[2].accuracy, null);
});

test('chord spread measures completed simultaneous heads, not missing members', () => {
  const judgments = [head('a', 0, 1_000_000, -2_000), head('b', 2, 1_000_000, 7_000), head('c', 0, 2_000_000, 0), head('d', 3, 2_000_000, null, 'MISS')];
  const analysis = analyzeRun({ judgments });
  assert.deepEqual(analysis.chords, { count: 2, measuredCount: 1, medianSpreadMs: 9 });
});

test('broken holds and weak section provide a bounded actionable practice recommendation', () => {
  const judgments = Array.from({ length: 4 }, (_, index) => head(String(index), index, 11_000_000 + index * 1_000_000, null, 'MISS'));
  judgments.push({ ...head('hold', 0, 18_000_000, null, 'MISS'), kind: 'hold' });
  const analysis = analyzeRun({ judgments }, { durationUs: 30_000_000, notes: [{ id: 'hold', timeUs: 12_000_000, endUs: 18_000_000 }] });
  assert.equal(analysis.holds.broken, 1);
  assert.equal(analysis.holds.successRate, 0);
  assert.equal(analysis.weakestSegment.startUs, 10_000_000);
  assert.equal(analysis.recommendations[0].fromUs, 8_000_000);
  assert.equal(analysis.recommendations[0].toUs, 22_000_000);
  assert.equal(analysis.recommendations.length, 2);
});

const record = (id, overrides = {}) => ({ id, createdAt: `2026-11-${id === 'a' ? '01' : '02'}T00:00:00.000Z`, songId: 'song', chartId: 'song-normal', chartVersion: '1', difficulty: 'normal', mode: 'standard', rate: 1, inputOffsetUs: 5000, settings: { keys: ['KeyD', 'KeyF', 'KeyJ', 'KeyK'], windowScale: 1 }, summary: { accuracy: id === 'a' ? 90 : 95 }, analysis: { timing: { maeMs: id === 'a' ? 18 : 10 }, missRate: id === 'a' ? 5 : 2 }, environment: { sampleRate: 48000, clockSource: 'outputTimestamp' }, completed: true, interrupted: false, ...overrides });

test('growth compares only complete standard runs with identical chart, rate, rule, offset and environment', () => {
  const runs = [record('b'), record('a'), record('practice', { mode: 'practice' }), record('interrupted', { interrupted: true }), record('unfinished', { completed: false }), record('offset', { inputOffsetUs: 7000 }), record('rate', { rate: 0.8 }), record('chart', { chartVersion: '2' }), record('window', { settings: { keys: ['KeyD', 'KeyF', 'KeyJ', 'KeyK'], windowScale: 2 } }), record('device', { environment: { sampleRate: 44100 } })];
  const comparison = compareRuns(runs);
  assert.equal(comparison.eligibleCount, 7);
  assert.equal(comparison.excludedCount, 3);
  assert.equal(comparison.groups.length, 6);
  const pair = comparison.groups.find(group => group.count === 2);
  assert.deepEqual(pair.records.map(run => run.id), ['a', 'b']);
  assert.equal(pair.accuracyDelta, 5);
  assert.equal(pair.maeDeltaMs, -8);
  assert.equal(pair.missRateDelta, -3);
  assert.equal(pair.bestAccuracy, 95);
  assert.equal(comparison.groups.find(group => group.count === 1).accuracyDelta, null);
});

test('CSV preserves true missing values, Korean/quotes/newlines and disables spreadsheet formulas', () => {
  const csv = toCSV({ ...record('a'), songId: '=HYPERLINK("evil")', summary: { accuracy: 0, judgments: [{ ...head('한글,"행\n', 0, 1_000_000, null, 'MISS') }] } });
  assert.ok(csv.startsWith('\uFEFF'));
  assert.ok(csv.includes('"\'=HYPERLINK(""evil"")"'));
  assert.ok(csv.includes('"한글,""행\n"'));
  assert.ok(csv.includes('"","MISS"'));
  assert.ok(csv.includes('\r\n'));
});
