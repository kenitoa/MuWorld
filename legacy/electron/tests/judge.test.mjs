import test from 'node:test';
import assert from 'node:assert/strict';
import { RhythmEngine, RULES } from '../src/core/judge.mjs';

const note = (id, lane, timeUs, endUs) => ({ id, lane, timeUs, ...(endUs === undefined ? {} : { endUs }), pattern: 'test' });
const chart = (notes, durationUs = 5_000_000) => ({ id: 'test', durationUs, notes });
const press = (engine, lane, timeUs) => engine.input(lane, true, timeUs);

test('5ms is applied exactly once, without changing the stored setting', () => {
  const engine = new RhythmEngine(chart([note('a', 0, 1_000_000)]), { inputOffsetUs: 5_000 });
  const hit = press(engine, 0, 1_007_000)[0];
  assert.equal(engine.inputOffsetUs, 5_000);
  assert.equal(hit.deltaUs, 2_000);
  assert.equal(hit.grade, 'PERFECT');
});

test('all symmetric grade boundaries include exact boundary and exclude the following microsecond', () => {
  const cases = [[20_000, 'PERFECT', 'GREAT'], [45_000, 'GREAT', 'GOOD'], [80_000, 'GOOD', 'BAD'], [120_000, 'BAD', null]];
  for (const [boundary, inside, outside] of cases) for (const sign of [-1, 1]) {
    const engine = new RhythmEngine(chart([note('a', 0, 1_000_000)]));
    const atBoundary = press(engine, 0, 1_000_000 + boundary * sign).find((item) => item.kind === 'head');
    assert.equal(atBoundary?.grade, inside, `${sign * boundary}`);
    const other = new RhythmEngine(chart([note('a', 0, 1_000_000)]));
    const beyond = press(other, 0, 1_000_000 + (boundary + 1) * sign).find((item) => item.grade !== 'MISS');
    assert.equal(beyond?.grade ?? null, outside, `${sign * (boundary + 1)}`);
  }
});

test('rate changes use real-time windows and exact fractional comparisons', () => {
  for (const rate of [0.5, 0.8, 1, 1.1, 1.25, 1.5, 2]) {
    const boundarySongUs = Math.round(20_000 * rate);
    const engine = new RhythmEngine(chart([note('a', 0, 1_000_000)]), { rate });
    const judgment = press(engine, 0, 1_000_000 + boundarySongUs)[0];
    assert.equal(judgment.grade, 'PERFECT', `rate ${rate}`);
    assert.equal(judgment.deltaUs, 20_000);
    const outside = new RhythmEngine(chart([note('a', 0, 1_000_000)]), { rate });
    assert.equal(press(outside, 0, 1_000_000 + boundarySongUs + 1)[0].grade, 'GREAT');
  }
});

test('offset stays in real microseconds at modified rates', () => {
  const engine = new RhythmEngine(chart([note('a', 0, 1_000_000)]), { rate: 0.8, inputOffsetUs: 5_000 });
  assert.equal(press(engine, 0, 1_004_000)[0].deltaUs, 0);
});

test('windowScale changes tolerance but not measured error', () => {
  const engine = new RhythmEngine(chart([note('a', 0, 1_000_000)]), { windowScale: 0.5 });
  const judgment = press(engine, 0, 1_020_000)[0];
  assert.equal(judgment.grade, 'GREAT');
  assert.equal(judgment.deltaUs, 20_000);
});

test('negative offset adjusts the miss deadline before the chart target', () => {
  const engine = new RhythmEngine(chart([note('a', 0, 1_000_000)]), { inputOffsetUs: -200_000 });
  assert.deepEqual(engine.advance(920_000), []);
  assert.equal(engine.notes[0].state, 'pending');
  const judgments = engine.advance(920_001);
  assert.equal(judgments.length, 1);
  assert.equal(judgments[0].grade, 'MISS');
  assert.equal(judgments[0].timeUs, 920_001);
});

test('negative fractional deadlines use floor rather than truncation', () => {
  const engine = new RhythmEngine(chart([note('a', 0, 0)]), { inputOffsetUs: -200_001, rate: 0.8 });
  assert.deepEqual(engine.advance(-64_001), []);
  assert.equal(engine.advance(-64_000)[0].grade, 'MISS');
});

test('earliest pending lane candidate wins and each event consumes one head', () => {
  const engine = new RhythmEngine(chart([note('first', 0, 1_000_000), note('second', 0, 1_030_000)]));
  assert.equal(press(engine, 0, 1_025_000)[0].noteId, 'first');
  assert.equal(engine.notes[1].state, 'pending');
  engine.input(0, false, 1_026_000);
  assert.equal(press(engine, 0, 1_030_000)[0].noteId, 'second');
});

test('repeat downs cannot hit later notes or inflate extra input count', () => {
  const engine = new RhythmEngine(chart([note('first', 0, 1_000_000), note('second', 0, 1_100_000)]));
  press(engine, 0, 1_000_000);
  assert.deepEqual(press(engine, 0, 1_100_000), []);
  engine.advance(1_220_001);
  assert.equal(engine.notes[1].state, 'miss');
  assert.equal(engine.extraInputs, 0);
  const other = new RhythmEngine(chart([note('a', 0, 1_000_000)]));
  press(other, 0, 0);
  press(other, 0, 1_000_000);
  assert.equal(other.extraInputs, 1);
  assert.equal(other.notes[0].state, 'pending');
});

test('subframe release/down and chords keep their original distinct event timestamps', () => {
  const engine = new RhythmEngine(chart([note('a', 0, 1_000_000), note('b', 0, 1_010_000), note('c', 1, 1_000_000)]));
  press(engine, 0, 1_000_000);
  engine.input(0, false, 1_001_000);
  press(engine, 0, 1_010_000);
  press(engine, 1, 1_003_000); // Delivered after the second same-lane hit.
  assert.deepEqual(engine.judgments.map((item) => [item.noteId, item.deltaUs]), [['a', 0], ['c', 3_000], ['b', 0]]);
});

test('held tail completes exactly at end and late release has no penalty', () => {
  const engine = new RhythmEngine(chart([note('hold', 0, 1_000_000, 2_000_000)]));
  press(engine, 0, 1_000_000);
  assert.equal(engine.notes[0].holdState, 'holding');
  assert.deepEqual(engine.advance(1_999_999), []);
  const tail = engine.advance(2_000_000)[0];
  assert.equal(tail.kind, 'hold');
  assert.equal(tail.grade, 'PERFECT');
  assert.equal(tail.deltaUs, null);
  engine.input(0, false, 2_100_000);
  assert.equal(engine.judgments.length, 2);
  assert.equal(engine.snapshot().score, 1_000_000);
});

test('release at exact corrected end succeeds, one microsecond early fails permanently', () => {
  for (const inputOffsetUs of [-5_000, 0, 5_000]) {
    const rawEnd = 2_000_000 + inputOffsetUs;
    const exact = new RhythmEngine(chart([note('hold', 0, 1_000_000, 2_000_000)]), { inputOffsetUs });
    press(exact, 0, 1_000_000 + inputOffsetUs);
    assert.equal(exact.input(0, false, rawEnd)[0].grade, 'PERFECT');
    const early = new RhythmEngine(chart([note('hold', 0, 1_000_000, 2_000_000)]), { inputOffsetUs });
    press(early, 0, 1_000_000 + inputOffsetUs);
    assert.equal(early.input(0, false, rawEnd - 1)[0].grade, 'MISS');
    press(early, 0, rawEnd);
    early.advance(rawEnd + 100_000);
    assert.equal(early.notes[0].holdState, 'broken');
    assert.equal(early.judgments.filter((item) => item.kind === 'hold').length, 1);
  }
});

test('fractional-rate hold tail and offset use exact deadline', () => {
  const engine = new RhythmEngine(chart([note('hold', 0, 1_000_000, 2_000_000)]), { rate: 0.8, inputOffsetUs: -5_001 });
  press(engine, 0, 995_999);
  assert.deepEqual(engine.advance(1_995_999), []);
  assert.equal(engine.advance(1_996_000)[0].grade, 'PERFECT');
});

test('missed hold head fails both units and cannot recover', () => {
  const engine = new RhythmEngine(chart([note('hold', 0, 1_000_000, 2_000_000)]));
  const missed = engine.advance(1_120_001);
  assert.deepEqual(missed.map((item) => [item.kind, item.grade]), [['head', 'MISS'], ['hold', 'MISS']]);
  press(engine, 0, 1_200_000);
  engine.advance(2_000_000);
  assert.equal(engine.snapshot().judgedUnits, 2);
  assert.equal(engine.notes[0].holdState, 'broken');
});

test('a late head after a short hold ended cannot earn sustain credit', () => {
  const engine = new RhythmEngine(chart([note('hold', 0, 1_000_000, 1_050_000)]));
  const result = press(engine, 0, 1_060_000);
  assert.deepEqual(result.map((item) => [item.kind, item.grade]), [['head', 'GOOD'], ['hold', 'MISS']]);
});

test('frame delay and previously emitted misses reconcile using original event timestamps', () => {
  const source = chart([note('a', 0, 1_000_000), note('h', 1, 1_100_000, 1_500_000), note('b', 0, 1_600_000)]);
  const inputs = [[0, true, 1_000_000], [0, false, 1_010_000], [1, true, 1_100_000], [1, false, 1_450_000], [0, true, 1_605_000]];
  const regular = new RhythmEngine(source);
  for (const event of inputs) regular.input(...event);
  const delayed = new RhythmEngine(source);
  delayed.advance(2_000_000);
  assert.equal(delayed.snapshot().counts.MISS, 4);
  const changed = delayed.input(...inputs[0]);
  assert.equal(changed[0].grade, 'PERFECT');
  for (const event of inputs.slice(1)) delayed.input(...event);
  assert.deepEqual(delayed.finish(), regular.finish());
});

test('a delayed early-release timestamp revokes provisional sustain success', () => {
  const engine = new RhythmEngine(chart([note('h', 0, 1_000_000, 2_000_000)]));
  press(engine, 0, 1_000_000);
  engine.advance(2_100_000);
  assert.equal(engine.snapshot().counts.PERFECT, 2);
  const revised = engine.input(0, false, 1_999_999);
  assert.equal(revised.length, 1);
  assert.equal(revised[0].kind, 'hold');
  assert.equal(revised[0].grade, 'MISS');
  assert.equal(engine.snapshot().counts.PERFECT, 1);
  assert.equal(engine.combo, 0);
  assert.equal(engine.maxCombo, 1);
});

test('exact-tail input after advance at the same timestamp has deterministic sustain and combo', () => {
  const source = chart([note('h', 0, 1_000_000, 1_020_000), note('a', 1, 1_020_000)]);
  const direct = new RhythmEngine(source);
  press(direct, 0, 1_020_000);
  press(direct, 1, 1_020_000);
  const advanced = new RhythmEngine(source);
  advanced.advance(1_020_000);
  press(advanced, 1, 1_020_000);
  press(advanced, 0, 1_020_000);
  assert.deepEqual(advanced.finish(), { ...direct.finish(), inputs: advanced.inputs });
  assert.equal(advanced.notes[0].holdState, 'complete');
});

test('replay result and combo are independent of 30/60/120/240fps and stalls', () => {
  const source = chart([note('a', 0, 1_000_000), note('h', 1, 1_000_000, 1_300_000), note('m', 2, 1_180_000), note('b', 0, 1_300_000)]);
  const events = [[0, true, 1_000_000], [1, true, 1_005_000], [0, false, 1_050_000], [1, false, 1_300_000], [0, true, 1_300_000]];
  const baseline = new RhythmEngine(source);
  for (const event of events) baseline.input(...event);
  const expected = baseline.finish();
  for (const fps of [30, 60, 120, 240]) {
    const engine = new RhythmEngine(source);
    let cursor = 0;
    for (let frame = 0; frame <= 2_000_000; frame += Math.round(1_000_000 / fps)) {
      const next = frame > 1_050_000 && frame < 1_400_000 ? 1_400_000 : frame;
      engine.advance(next); // Deliberately expire before draining the input queue.
      while (cursor < events.length && events[cursor][2] <= next) engine.input(...events[cursor++]);
    }
    assert.deepEqual(engine.finish(), expected, `${fps}fps`);
  }
});

test('practice excludes notes crossing boundaries and does not invent partial holds', () => {
  const original = chart([note('before', 0, 100_000, 600_000), note('start', 1, 500_000), note('inside', 2, 700_000, 1_500_000), note('cross', 3, 900_000, 1_600_000), note('end', 0, 1_500_000)]);
  const engine = new RhythmEngine(original, { fromUs: 500_000, toUs: 1_500_000 });
  assert.deepEqual(engine.notes.map((item) => item.id), ['start', 'inside']);
  assert.equal(engine.totalUnits, 3);
  assert.equal(original.notes[0].state, undefined);
});

test('weights, score, extra presses, and snapshot isolation', () => {
  const engine = new RhythmEngine(chart([note('p', 0, 500_000), note('g', 1, 1_000_000), note('o', 2, 1_500_000), note('b', 3, 2_000_000), note('m', 0, 2_500_000)]));
  press(engine, 0, 500_000);
  press(engine, 1, 1_030_000);
  press(engine, 2, 1_560_000);
  press(engine, 3, 2_100_000);
  const summary = engine.finish();
  assert.deepEqual(RULES.weights, { PERFECT: 1, GREAT: 0.9, GOOD: 0.6, BAD: 0.2, MISS: 0 });
  assert.equal(summary.accuracy, 54);
  assert.equal(summary.score, 540_000);
  assert.equal(summary.maxCombo, 4);
  assert.equal(summary.combo, 0);
  summary.judgments[0].grade = 'MISS';
  summary.inputs.length = 0;
  assert.equal(engine.snapshot().counts.PERFECT, 1);
  assert.equal(engine.inputs.length, 4);
  assert.deepEqual(press(engine, 1, 3_000_000), []);
  assert.deepEqual(engine.advance(4_000_000), []);
});

test('empty and invalid input handling is safe', () => {
  const engine = new RhythmEngine(chart([]));
  assert.equal(engine.finish().accuracy, 0);
  assert.equal(engine.snapshot().score, 0);
  assert.equal(engine.snapshot().totalUnits, 0);
  assert.throws(() => engine.input(4, true, 0), RangeError);
  assert.throws(() => engine.input(0, 1, 0), TypeError);
  assert.throws(() => engine.advance(0.5), TypeError);
  assert.throws(() => new RhythmEngine(chart([]), { rate: 0 }), RangeError);
  assert.throws(() => new RhythmEngine(chart([]), { inputOffsetUs: 5.5 }), TypeError);
  assert.throws(() => new RhythmEngine(chart([]), { fromUs: 3, toUs: 2 }), RangeError);
});
