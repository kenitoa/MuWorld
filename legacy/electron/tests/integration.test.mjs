import test from 'node:test';
import assert from 'node:assert/strict';
import { AudioSystem } from '../src/core/audio.mjs';
import { RhythmEngine } from '../src/core/judge.mjs';
import { analyzeRun } from '../src/core/analytics.mjs';
import { DEFAULT_SETTINGS, saveRecord, loadRecords } from '../src/core/storage.mjs';

const chart = {
  id: 'integration-v1', songId: 'integration', difficulty: 'normal', version: '1', durationUs: 10_000_000,
  notes: [{ id: 'tap', lane: 0, timeUs: 4_000_000, pattern: 'single' }, { id: 'hold', lane: 1, timeUs: 5_000_000, endUs: 6_000_000, pattern: 'hold' }],
};
function transport({ rate = 1, fromUs = 0, toUs = chart.durationUs } = {}) {
  const audio = new AudioSystem();
  // The device is currently at 103 s / performance 4000 ms. A queued input can
  // still refer to an earlier original performance timestamp.
  audio.context = { getOutputTimestamp: () => ({ contextTime: 103, performanceTime: 4000 }), currentTime: 103.1, outputLatency: .08, baseLatency: .02 };
  audio._state = 'running'; audio._ctxStart = 100; audio._rate = rate; audio._fromUs = fromUs; audio._toUs = toUs;
  return audio;
}
const performanceAt = (songUs, rate, fromUs = 0) => 1000 + (songUs - fromUs) / rate / 1000;

test('audio timestamp → song time → real-time offset keeps +5ms exact across rates', () => {
  for (const rate of [.5, .8, 1, 1.25]) {
    const audio = transport({ rate, fromUs: 4_000_000 });
    const engine = new RhythmEngine(chart, { rate, inputOffsetUs: 5000, fromUs: 4_000_000 });
    const timestamp = performanceAt(4_000_000 + 5000 * rate, rate, 4_000_000);
    const originalSongUs = audio.songTimeUs(timestamp);
    const judgment = engine.input(0, true, originalSongUs)[0];
    assert.equal(judgment.deltaUs, 0, `${rate}x`);
    assert.equal(judgment.grade, 'PERFECT');
    assert.equal(originalSongUs, 4_000_000 + 5000 * rate);
  }
});

test('historical event timestamps survive delayed frame processing without double latency subtraction', () => {
  const audio = transport();
  const engine = new RhythmEngine(chart, { inputOffsetUs: 5000 });
  engine.advance(audio.songTimeUs(performanceAt(4_500_000, 1)));
  assert.equal(engine.notes[0].state, 'miss');
  const historical = audio.songTimeUs(performanceAt(4_005_000, 1));
  assert.equal(historical, 4_005_000);
  assert.equal(engine.input(0, true, historical)[0].grade, 'PERFECT');
  assert.equal(engine.snapshot().counts.MISS, 0);
});

test('practice lead-in, offset tail completion and playback rate share one original-song timeline', () => {
  const rate = .8, fromUs = 4_000_000, audio = transport({ rate, fromUs });
  const engine = new RhythmEngine(chart, { rate, fromUs, toUs: 6_000_000, inputOffsetUs: -5000 });
  assert.equal(audio.songTimeUs(500), 3_600_000);
  assert.deepEqual(engine.advance(audio.songTimeUs(500)), []);
  engine.input(0, true, audio.songTimeUs(performanceAt(3_996_000, rate, fromUs)));
  engine.input(0, false, audio.songTimeUs(performanceAt(4_100_000, rate, fromUs)));
  engine.input(1, true, audio.songTimeUs(performanceAt(4_996_000, rate, fromUs)));
  const tail = engine.input(1, false, audio.songTimeUs(performanceAt(5_996_000, rate, fromUs)));
  assert.equal(tail[0].grade, 'PERFECT');
  assert.equal(tail[0].kind, 'hold');
  assert.equal(engine.finish().score, 1_000_000);
});

test('transport continues after the audio interval so expanded late windows can resolve', () => {
  const nearEnd = { ...chart, notes: [{ id: 'last', lane: 0, timeUs: 9_999_000, pattern: 'single' }] };
  const audio = transport({ rate: .8 });
  const engine = new RhythmEngine(nearEnd, { rate: .8, windowScale: 2 });
  const postrollUs = audio.songTimeUs(performanceAt(10_184_000, .8));
  assert.equal(postrollUs, 10_184_000);
  assert.ok(postrollUs > chart.durationUs);
  const judgment = engine.input(0, true, postrollUs)[0];
  assert.equal(judgment.grade, 'BAD');
  assert.equal(judgment.deltaUs, 231_250);
});

test('finished engine → analysis → local storage → replay preserves the authoritative result', () => {
  const backing = new Map();
  const originalDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
  Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: { getItem: key => backing.get(key) ?? null, setItem: (key, value) => backing.set(key, String(value)), removeItem: key => backing.delete(key) } });
  try {
    const options = { rate: .8, inputOffsetUs: 5000, fromUs: 4_000_000, toUs: 6_000_000 };
    const audio = transport(options), engine = new RhythmEngine(chart, options);
    const events = [[0, true, 4_004_000], [0, false, 4_030_000], [1, true, 5_008_000], [1, false, 6_004_000]];
    for (const [lane, pressed, songUs] of events) engine.input(lane, pressed, audio.songTimeUs(performanceAt(songUs, .8, 4_000_000)));
    const summary = engine.finish();
    const record = { id: 'integration-run', createdAt: '2026-11-01T00:00:00.000Z', songId: chart.songId, chartId: chart.id, chartVersion: chart.version, difficulty: chart.difficulty, mode: 'practice', rate: options.rate, inputOffsetUs: options.inputOffsetUs,
      settings: { ...DEFAULT_SETTINGS, keys: [...DEFAULT_SETTINGS.keys], inputOffsetUs: options.inputOffsetUs }, summary, analysis: analyzeRun(summary, chart), environment: { sampleRate: 48000, clockSource: 'output-timestamp' }, completed: true, interrupted: false, fromUs: options.fromUs, toUs: options.toUs };
    saveRecord(record);
    const restored = loadRecords().find(item => item.id === record.id);
    assert.deepEqual(restored.summary, summary);
    assert.equal(restored.analysis.timing.count, 2);
    const replay = new RhythmEngine(chart, { inputOffsetUs: restored.inputOffsetUs, rate: restored.rate, fromUs: restored.fromUs, toUs: restored.toUs });
    replay.advance(6_500_000); // A stalled renderer may expire notes before draining replay events.
    for (const event of restored.summary.inputs) replay.input(event.lane, event.pressed, event.timeUs);
    assert.deepEqual(replay.finish(), summary);
  } finally {
    if (originalDescriptor) Object.defineProperty(globalThis, 'localStorage', originalDescriptor);
    else delete globalThis.localStorage;
  }
});
