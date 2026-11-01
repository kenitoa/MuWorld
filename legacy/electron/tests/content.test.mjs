import test from 'node:test';
import assert from 'node:assert/strict';
import { SONGS, DIFFICULTIES, getChart, getDrills, validateChart, beatToUs } from '../src/core/content.mjs';
import { mapOutputTimestamp, AudioSystem } from '../src/core/audio.mjs';

test('five original tracks provide fifteen deterministic, valid charts', () => {
  assert.equal(SONGS.length, 5);
  assert.equal(DIFFICULTIES.length, 3);
  for (const song of SONGS) {
    assert.ok(song.durationSec >= 60 && song.durationSec <= 100, song.id);
    let previousCount = 0;
    for (const difficulty of DIFFICULTIES) {
      const chart = getChart(song.id, difficulty.id);
      assert.deepEqual(validateChart(chart), [], chart.id);
      assert.ok(chart.notes.length > previousCount, chart.id);
      previousCount = chart.notes.length;
      assert.deepEqual(chart, getChart(song.id, difficulty.id));
      assert.equal(new Set(chart.notes.map(note => note.id)).size, chart.notes.length);
    }
  }
});

test('chart note onsets and holds are on the soundtrack sixteenth-note grid', () => {
  for (const song of SONGS) {
    for (const difficulty of DIFFICULTIES) {
      for (const note of getChart(song.id, difficulty.id).notes) {
        for (const time of [note.timeUs, note.endUs].filter(value => value !== undefined)) {
          const quarterBeat = Math.round(time * song.bpm / 60_000_000 * 4) / 4;
          assert.equal(time, beatToUs(song, quarterBeat));
        }
      }
    }
  }
});

test('each chart call is safe to annotate without changing source content', () => {
  const first = getChart('glasshouse');
  first.notes[0].timeUs = -99;
  first.notes.pop();
  const second = getChart('glasshouse');
  assert.ok(second.notes[0].timeUs > 0);
  assert.notEqual(first.notes.length, second.notes.length);
});

test('twelve focused drills retain original song timestamps and legal holds', () => {
  const drills = getDrills();
  assert.equal(drills.length, 12);
  assert.equal(new Set(drills.map(drill => drill.id)).size, 12);
  for (const drill of drills) {
    assert.deepEqual(validateChart(drill), [], drill.id);
    assert.ok(drill.notes.length >= 4);
    assert.ok(drill.notes.every(note => note.timeUs >= drill.fromUs && (note.endUs ?? note.timeUs) <= drill.toUs));
    assert.ok(drill.durationSec > 8 && drill.durationSec < 20);
  }
});

test('invalid imports and requests are rejected', () => {
  assert.throws(() => getChart('missing'), /Unknown song/);
  assert.throws(() => getChart('glasshouse', 'impossible'), /Unknown difficulty/);
  const malformed = { durationUs: 1000, notes: [
    { id: 'a', lane: 0, timeUs: 100, endUs: 800 },
    { id: 'b', lane: 0, timeUs: 500 },
    { id: 'b', lane: 4, timeUs: 2000 },
  ] };
  const errors = validateChart(malformed);
  assert.ok(errors.some(message => message.includes('Overlapping')));
  assert.ok(errors.some(message => message.includes('Duplicate')));
  assert.ok(errors.some(message => message.includes('Invalid lane')));
});

test('audio output timestamp maps event time without double-subtracting latency', () => {
  const timestamp = { contextTime: 1.25, performanceTime: 2000 };
  assert.equal(mapOutputTimestamp(timestamp, 2005), 1.255);
  assert.equal(mapOutputTimestamp(timestamp, 1995), 1.245);
});

test('audio song mapping preserves original song timeline at reduced speed', () => {
  const audio = new AudioSystem();
  audio.context = { getOutputTimestamp: () => ({ contextTime: 12, performanceTime: 4000 }) };
  audio._state = 'running'; audio._ctxStart = 10; audio._fromUs = 4_000_000; audio._toUs = 20_000_000; audio._rate = .5;
  assert.equal(audio.songTimeUs(4000), 5_000_000);
  assert.equal(audio.songTimeUs(4010), 5_005_000);
  assert.equal(audio._clockSource, 'output-timestamp');
  audio._state = 'paused'; audio._pausedUs = 5_000_000;
  assert.equal(audio.songTimeUs(999999), 5_000_000);
});

test('audio lead-in and trailing judgment window retain their actual timestamps', () => {
  const audio = new AudioSystem();
  audio.context = { getOutputTimestamp: () => ({ contextTime: 1, performanceTime: 1000 }) };
  audio._state = 'running'; audio._ctxStart = 3; audio._fromUs = 0; audio._toUs = 10_000_000; audio._rate = 1;
  assert.equal(audio.songTimeUs(1000), -2_000_000);
  assert.equal(audio.songTimeUs(20000), 17_000_000);
});

test('pause and resume preserve a silent postroll clock beyond the source endpoint', async () => {
  const audio = new AudioSystem();
  let outputClock = 10.15;
  audio.context = { state: 'running', sampleRate: 48000 };
  audio._outputTime = () => outputClock;
  audio._state = 'running'; audio._ctxStart = 0; audio._fromUs = 0; audio._toUs = 10_000_000; audio._rate = 1;
  audio._config = { songId: 'glasshouse', rate: 1, fromUs: 0, toUs: 10_000_000 };
  assert.equal(audio.pause(), 10_150_000);
  outputClock = 12;
  assert.equal(audio.songTimeUs(), 10_150_000);
  const resumed = await audio.resume();
  assert.equal(resumed.postRoll, true);
  assert.equal(audio.getInfo().state, 'running');
  assert.equal(audio.songTimeUs(), 10_150_000);
  outputClock = 12.2;
  assert.equal(audio.songTimeUs(), 10_350_000);
  assert.equal(audio.pause(), 10_350_000);
  outputClock = 15;
  assert.equal((await audio.resume()).postRoll, true);
  outputClock = 15.1;
  assert.equal(audio.songTimeUs(), 10_450_000);
  assert.equal(audio._source, null);
});

test('a stop while silent postroll resume is initializing cancels the resume', async () => {
  const audio = new AudioSystem();
  audio._state = 'paused'; audio._pausedUs = 10_150_000; audio._toUs = 10_000_000;
  audio._config = { songId: 'glasshouse', rate: 1 };
  let release;
  audio.init = () => new Promise(resolve => { release = resolve; });
  const resuming = audio.resume();
  audio.stop(); release();
  assert.equal(await resuming, null);
  assert.equal(audio.getInfo().state, 'idle');
});
