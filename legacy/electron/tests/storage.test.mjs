import test from 'node:test';
import assert from 'node:assert/strict';

class MemoryStorage {
  constructor() { this.values = new Map(); this.failWrites = false; }
  getItem(key) { return this.values.get(key) ?? null; }
  setItem(key, value) { if (this.failWrites) throw new Error('QuotaExceededError'); this.values.set(key, String(value)); }
  removeItem(key) { this.values.delete(key); }
}
let moduleId = 0;
async function setup(storage = new MemoryStorage()) {
  Object.defineProperty(globalThis, 'localStorage', { value: storage, configurable: true, writable: true });
  return { api: await import(`../src/core/storage.mjs?test=${++moduleId}`), storage };
}
function fixture(api, id = 'run-1') {
  return {
    id, createdAt: '2026-11-01T12:00:00.000Z', songId: 'song', chartId: 'song-normal', chartVersion: '1', difficulty: 'normal', mode: 'standard', rate: 1, inputOffsetUs: 5000,
    settings: { ...api.DEFAULT_SETTINGS, keys: [...api.DEFAULT_SETTINGS.keys], inputOffsetUs: 5000 },
    summary: { accuracy: 100, score: 1_000_000, combo: 1, maxCombo: 1, totalUnits: 1, judgedUnits: 1, extraInputs: 0, counts: { PERFECT: 1, GREAT: 0, GOOD: 0, BAD: 0, MISS: 0 }, judgments: [{ noteId: 'note', lane: 0, timeUs: 1_005_000, targetUs: 1_000_000, deltaUs: 0, grade: 'PERFECT', kind: 'head', pattern: 'single' }], inputs: [{ lane: 0, pressed: true, timeUs: 1_005_000 }] },
    analysis: { timing: { count: 1, meanMs: 0 } }, environment: { sampleRate: 48000, clockSource: 'outputTimestamp' }, completed: true, interrupted: false,
  };
}

test('settings preserve exact integer microsecond offsets and return isolated copies', async () => {
  const { api } = await setup();
  const saved = api.saveSettings({ inputOffsetUs: 5000, visualOffsetUs: -7001, keys: ['KeyA', 'KeyS', 'KeyK', 'KeyL'] });
  assert.equal(saved.inputOffsetUs, 5000);
  assert.equal(api.loadSettings().visualOffsetUs, -7001);
  saved.keys[0] = 'KeyZ';
  assert.equal(api.loadSettings().keys[0], 'KeyA');
  assert.equal(api.DEFAULT_SETTINGS.keys[0], 'KeyD');
  assert.throws(() => api.saveSettings({ inputOffsetUs: 5000.5 }), /허용 범위/);
  assert.equal(api.loadSettings().inputOffsetUs, 5000);
});

test('invalid settings fail without writing or silently clamping values', async () => {
  const { api, storage } = await setup();
  api.saveSettings({ volume: 0.5 });
  const before = storage.getItem('muworld:v1');
  for (const partial of [{ volume: 2 }, { volume: NaN }, { effects: -1 }, { practiceRate: 0.1 }, { approachMs: 0 }, { inputOffsetUs: 500_001 }, { keys: ['KeyD', 'KeyD', 'KeyJ', 'KeyK'] }, { showTiming: 'true' }, { preset: 'unknown' }, { practiceFromSec: 30, practiceToSec: 20 }, { unexpected: true }]) assert.throws(() => api.saveSettings(partial), RangeError);
  assert.equal(storage.getItem('muworld:v1'), before);
});

test('5ms precision practice preserves the exact 0.25 window scale in settings, records and backups', async () => {
  const { api, storage } = await setup();
  api.saveSettings({ windowScale: 0.25 });
  assert.equal(api.loadSettings().windowScale, 0.25);
  const run = fixture(api, 'precision-run');
  run.mode = 'practice';
  run.settings.windowScale = 0.25;
  api.saveRecord(run);
  const fresh = await import(`../src/core/storage.mjs?test=${++moduleId}`);
  assert.equal(fresh.loadSettings().windowScale, 0.25);
  assert.equal(fresh.loadRecords()[0].settings.windowScale, 0.25);
  const backup = fresh.exportBackup();
  fresh.saveSettings({ windowScale: 1 });
  fresh.importBackup(backup);
  assert.equal(fresh.loadSettings().windowScale, 0.25);
  assert.equal(fresh.loadRecords()[0].settings.windowScale, 0.25);
  const before = storage.getItem('muworld:v1');
  assert.throws(() => fresh.saveSettings({ windowScale: 0.249 }), /허용 범위/);
  assert.equal(storage.getItem('muworld:v1'), before);
});

test('record CRUD and backup round-trip preserve the real inputs and 5ms setting', async () => {
  const { api } = await setup();
  const original = fixture(api);
  api.saveRecord(original);
  original.summary.judgments[0].deltaUs = 7000;
  assert.equal(api.loadRecords()[0].summary.judgments[0].deltaUs, 0);
  assert.equal(api.loadRecords()[0].inputOffsetUs, 5000);
  const exported = api.exportBackup();
  api.clearRecords();
  assert.equal(api.loadRecords().length, 0);
  assert.deepEqual(api.importBackup(exported).importedRecords, 1);
  api.saveRecord({ ...fixture(api), summary: { ...fixture(api).summary } });
  assert.equal(api.loadRecords().length, 1);
  assert.equal(api.deleteRecord('run-1'), true);
  assert.equal(api.deleteRecord('run-1'), false);
});

test('all imported records are validated before settings or existing data can change', async () => {
  const { api, storage } = await setup();
  api.saveSettings({ inputOffsetUs: 5000 });
  api.saveRecord(fixture(api));
  const before = storage.getItem('muworld:v1');
  const incoming = JSON.parse(api.exportBackup());
  incoming.settings.inputOffsetUs = 7000;
  incoming.records.push({ ...fixture(api, 'bad-run'), inputOffsetUs: 123.5 });
  assert.throws(() => api.importBackup(JSON.stringify(incoming)), /보정값/);
  assert.equal(storage.getItem('muworld:v1'), before);
  assert.equal(api.loadSettings().inputOffsetUs, 5000);
  assert.equal(api.loadRecords().length, 1);
  assert.throws(() => api.importBackup('not json'), /JSON/);
  assert.throws(() => api.importBackup(JSON.stringify({ ...incoming, version: 999 })), /버전/);
  const duplicate = JSON.parse(api.exportBackup());
  duplicate.records.push(duplicate.records[0]);
  assert.throws(() => api.importBackup(JSON.stringify(duplicate)), /중복/);
});

test('malformed and contradictory judgment data cannot enter records', async () => {
  const { api } = await setup();
  const invalid = fixture(api);
  invalid.summary.judgments[0].grade = 'MISS';
  invalid.summary.judgments[0].deltaUs = null;
  assert.throws(() => api.saveRecord(invalid), /집계/);
  const noDelta = fixture(api);
  noDelta.summary.judgments[0].deltaUs = null;
  assert.throws(() => api.saveRecord(noDelta), /편차/);
  const wrongOffset = fixture(api);
  wrongOffset.settings.inputOffsetUs = 7000;
  assert.throws(() => api.saveRecord(wrongOffset), /보정값/);
  const nullId = fixture(api);
  nullId.summary.judgments[0].noteId = null;
  assert.throws(() => api.saveRecord(nullId), /개별 판정/);
  assert.equal(api.loadRecords().length, 0);
});

test('last valid envelope recovers when primary storage becomes corrupt', async () => {
  const { api, storage } = await setup();
  api.saveSettings({ inputOffsetUs: 5000 });
  api.saveSettings({ inputOffsetUs: 7000 });
  storage.setItem('muworld:v1', '{broken');
  const fresh = await import(`../src/core/storage.mjs?test=${++moduleId}`);
  assert.equal(fresh.loadSettings().inputOffsetUs, 5000);
  fresh.saveSettings({ visualOffsetUs: -3000 });
  assert.equal(fresh.loadSettings().inputOffsetUs, 5000);
});

test('quota failures keep the previous primary intact and report the failure', async () => {
  const { api, storage } = await setup();
  api.saveSettings({ inputOffsetUs: 5000 });
  const before = storage.getItem('muworld:v1');
  storage.failWrites = true;
  assert.throws(() => api.saveSettings({ inputOffsetUs: 7000 }), /기존 데이터는 유지/);
  assert.equal(storage.getItem('muworld:v1'), before);
  assert.equal(api.loadSettings().inputOffsetUs, 5000);
});

test('unavailable localStorage has an isolated in-memory fallback', async () => {
  const { api } = await setup(null);
  api.saveSettings({ inputOffsetUs: -5000 });
  assert.equal(api.loadSettings().inputOffsetUs, -5000);
  api.saveRecord(fixture(api));
  assert.equal(api.loadRecords().length, 1);
});

test('prototype pollution and untrusted schema imports are rejected atomically', async () => {
  const { api } = await setup();
  const incoming = JSON.parse(api.exportBackup());
  incoming.records = [fixture(api)];
  incoming.records[0].environment = JSON.parse('{"__proto__":{"polluted":true}}');
  assert.throws(() => api.importBackup(JSON.stringify(incoming)), /허용되지 않는/);
  assert.equal({}.polluted, undefined);
  assert.equal(api.loadRecords().length, 0);
  assert.throws(() => api.importBackup(JSON.stringify({ ...incoming, schema: 'another-product' })), /MuWorld/);
});

test('real judgment snapshots round-trip including negative lead-in inputs and null sustain deltas', async () => {
  const { RhythmEngine } = await import('../src/core/judge.mjs');
  const { analyzeRun } = await import('../src/core/analytics.mjs');
  const { api } = await setup();
  const chart = { durationUs: 2_000_000, notes: [{ id: 'hold', lane: 0, timeUs: 0, endUs: 1_000_000, pattern: 'hold' }, { id: 'tap', lane: 1, timeUs: 500_000, pattern: 'single' }] };
  const engine = new RhythmEngine(chart, { inputOffsetUs: -5000, rate: 0.8 });
  engine.input(0, true, -4000);
  engine.input(1, true, 496_000);
  engine.advance(1_000_000);
  const summary = engine.finish();
  const run = { ...fixture(api), mode: 'practice', rate: 0.8, inputOffsetUs: -5000, settings: { ...api.DEFAULT_SETTINGS, inputOffsetUs: -5000 }, summary, analysis: analyzeRun(summary, chart) };
  api.saveRecord(run);
  const loaded = api.loadRecords()[0];
  assert.equal(loaded.summary.accuracy, 100);
  assert.equal(loaded.summary.inputs[0].timeUs, -4000);
  assert.equal(loaded.summary.judgments.find(event => event.kind === 'hold').deltaUs, null);
  assert.equal(loaded.analysis.timing.count, 2);
  assert.equal(loaded.analysis.holds.successRate, 100);
});
