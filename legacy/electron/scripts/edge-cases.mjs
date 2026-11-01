import { _electron as electron } from 'playwright';
import { mkdir, mkdtemp, writeFile } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';

const root = process.cwd();
await mkdir(path.join(root, 'artifacts'), { recursive: true });
const profile = await mkdtemp(path.join(root, 'artifacts', 'edge-profile-'));
const env = { ...process.env, MUWORLD_TEST: '1', MUWORLD_DATA_DIR: profile };
delete env.ELECTRON_RUN_AS_NODE;
const desktop = await electron.launch({ args: ['.'], cwd: root, env, timeout: 60_000 });
const page = await desktop.firstWindow();
const errors = [], results = [];
page.on('pageerror', error => errors.push(error.message));
const waitSong = async timeUs => {
  await page.waitForFunction(target => !__muworld.session || __muworld.audio.songTimeUs() >= target, timeUs, { timeout: 15_000, polling: 8 });
};
const reset = async () => page.evaluate(() => {
  __muworld.stopGame();
  __muworld.navigate('library');
  __muworld.updateSettings({ inputOffsetUs: 0, windowScale: 1, autoStep: false });
  __muworld.selected = 'glasshouse';
  __muworld.difficulty = 'easy';
});
const startShort = async ({ windowScale = 1, toUs = 4_200_000, notes = null, mode = 'practice' } = {}) => {
  await page.evaluate(async ({ windowScale, toUs, notes, mode }) => {
    const app = __muworld;
    app.updateSettings({ windowScale, inputOffsetUs: 0 });
    const chart = { ...app.chart(), id: 'edge-short-v1', notes: notes || [{ id: 'edge-tap', lane: 0, timeUs: 4_100_000, pattern: 'single' }] };
    await app.startGame({ mode, chart, rate: 1, fromUs: 4_000_000, toUs });
  }, { windowScale, toUs, notes, mode });
  await page.waitForSelector('#game-canvas');
};
const run = async (name, body) => {
  const started = Date.now();
  try {
    await reset();
    const evidence = await body();
    results.push({ name, passed: true, elapsedMs: Date.now() - started, evidence });
    console.log(`PASS ${name}`);
  } catch (error) {
    results.push({ name, passed: false, elapsedMs: Date.now() - started, error: error.stack || error.message });
    console.log(`FAIL ${name}: ${error.message}`);
  }
};

try {
  await page.waitForSelector('.library-layout');
  await page.waitForFunction(() => Boolean(window.__muworld));

  await run('drill completion keeps public library and practice difficulty valid', async () => {
    await page.evaluate(async () => {
      const app = __muworld;
      app.navigate('practice');
      await app.action('drill', { dataset: { index: '0' } });
      const original = app.session.chart;
      const first = original.notes[0];
      const short = { ...original, notes: [first] };
      await app.startGame({ mode: 'practice', chart: short, rate: 1, fromUs: first.timeUs - 100_000, toUs: first.timeUs + 100_000 });
    });
    await page.waitForSelector('.result-hero', { timeout: 15_000 });
    const record = await page.evaluate(() => ({ difficulty: __muworld.difficulty, recordDifficulty: __muworld.result.difficulty, completed: __muworld.result.completed }));
    assert.equal(record.recordDifficulty, 'drill');
    assert.equal(record.completed, true);
    assert.ok(['easy', 'normal', 'hard'].includes(record.difficulty));
    await page.evaluate(() => __muworld.navigate('library'));
    assert.equal(await page.locator('.library-layout').count(), 1);
    await page.evaluate(() => __muworld.navigate('practice'));
    assert.equal(await page.locator('.practice-layout').count(), 1);
    assert.equal(await page.locator('.drill-card').count(), 12);
    return record;
  });

  await run('expanded 240ms practice window survives beyond the old 200ms finish', async () => {
    await startShort({ windowScale: 2, notes: [{ id: 'edge-last', lane: 0, timeUs: 4_199_000, pattern: 'single' }] });
    await waitSong(4_410_000);
    const before = await page.evaluate(() => ({ active: Boolean(__muworld.session), timeUs: __muworld.audio.songTimeUs(), state: __muworld.session?.engine.notes[0].state }));
    assert.equal(before.active, true, 'the expanded late-hit window must remain open');
    assert.equal(before.state, 'pending');
    await page.keyboard.press('d');
    const hit = await page.evaluate(() => __muworld.session?.engine.judgments.find(judgment => judgment.noteId === 'edge-last' && judgment.kind === 'head'));
    assert.equal(hit?.grade, 'BAD', 'a real keyboard event between +200 and +240ms must still hit');
    assert.ok(hit.deltaUs > 200_000 && hit.deltaUs <= 240_000);
    await page.waitForSelector('.result-hero', { timeout: 5000 });
    return { before, hit };
  });

  await run('post-roll pause and silent resume reach the result screen', async () => {
    await startShort();
    await waitSong(4_240_000);
    const paused = await page.evaluate(() => {
      const app = __muworld;
      app.pauseGame('QA post-roll');
      return { paused: app.session?.paused, positionUs: app.audio.songTimeUs(), toUs: app.session?.toUs };
    });
    assert.equal(paused.paused, true);
    assert.ok(paused.positionUs > paused.toUs);
    await page.evaluate(() => __muworld.resumeGame());
    const resumed = await page.evaluate(() => ({ paused: __muworld.session?.paused, state: __muworld.audio.getInfo().state, gate: __muworld.session?.resumeGate }));
    assert.equal(resumed.paused, false);
    assert.equal(resumed.state, 'running');
    assert.equal(resumed.gate, false);
    await page.waitForSelector('.result-hero', { timeout: 5000 });
    assert.equal(await page.evaluate(() => __muworld.result.interrupted), true);
    return { paused, resumed };
  });

  await run('resume countdown ignores actual keyboard down and up', async () => {
    await startShort({ toUs: 6_000_000, notes: [{ id: 'edge-later', lane: 0, timeUs: 5_500_000, pattern: 'single' }] });
    await waitSong(4_200_000);
    await page.evaluate(() => __muworld.pauseGame('QA normal pause'));
    await page.evaluate(() => __muworld.resumeGame());
    const before = await page.evaluate(() => ({ gate: __muworld.session.resumeGate, inputs: __muworld.session.engine.inputs.length, timeUs: __muworld.audio.songTimeUs(), leadStart: __muworld.session.leadStart }));
    assert.equal(before.gate, true);
    assert.ok(before.timeUs < before.leadStart);
    await page.keyboard.press('d');
    assert.equal(await page.evaluate(() => __muworld.session.engine.inputs.length), before.inputs);
    await page.waitForFunction(() => __muworld.session && !__muworld.session.resumeGate, null, { timeout: 5000 });
    await page.keyboard.press('d');
    assert.equal(await page.evaluate(() => __muworld.session.engine.inputs.length), before.inputs + 2);
    return before;
  });

  await run('pausing a replay preserves original hold state and input log', async () => {
    await page.evaluate(async () => {
      const app = __muworld;
      const chart = { ...app.chart(), id: 'edge-replay-v1', notes: [{ id: 'edge-replay-hold', lane: 0, timeUs: 4_100_000, endUs: 4_900_000, pattern: 'hold' }] };
      const record = { ...app.result, settings: structuredClone(app.settings), rate: 1, summary: { ...app.result.summary, inputs: [{ lane: 0, pressed: true, timeUs: 4_100_000 }, { lane: 0, pressed: false, timeUs: 4_900_000 }] } };
      await app.startGame({ mode: 'replay', chart, record, rate: 1, fromUs: 4_000_000, toUs: 5_000_000 });
    });
    await waitSong(4_250_000);
    const state = await page.evaluate(() => {
      const app = __muworld;
      // Hidden native windows may not deliver the next rendering callback at the
      // polling checkpoint. Drain one real app frame before inspecting playback.
      cancelAnimationFrame(app.raf);
      app.loop(performance.now());
      const s = app.session;
      const before = { inputs: s.engine.inputs.length, holdState: s.engine.notes[0].holdState, held: [...s.held] };
      app.pauseGame('QA replay pause');
      const after = { inputs: s.engine.inputs.length, holdState: s.engine.notes[0].holdState, held: [...s.held] };
      return { before, after };
    });
    assert.equal(state.before.holdState, 'holding');
    assert.deepEqual(state.after, state.before);
    await page.evaluate(() => __muworld.resumeGame());
    const resumed = await page.evaluate(() => ({ inputs: __muworld.session.engine.inputs.length, holdState: __muworld.session.engine.notes[0].holdState }));
    assert.equal(resumed.inputs, state.before.inputs);
    assert.equal(resumed.holdState, 'holding');
    return { ...state, resumed };
  });

  await run('concurrent start requests schedule audio exactly once', async () => {
    const outcome = await page.evaluate(async () => {
      const app = __muworld, original = app.audio.start;
      let calls = 0;
      app.audio.start = async function (...args) { calls++; return original.apply(this, args); };
      const chart = { ...app.chart(), notes: [{ id: 'edge-once', lane: 0, timeUs: 4_100_000, pattern: 'single' }] };
      const versionBefore = app.startVersion || 0;
      const config = { mode: 'practice', chart, rate: 1, fromUs: 4_000_000, toUs: 4_200_000 };
      try {
        await Promise.all([app.startGame(config), app.startGame(config), app.startGame(config)]);
        return { calls, versions: app.startVersion - versionBefore, active: Boolean(app.session), starting: app.startingGame };
      } finally { app.audio.start = original; }
    });
    assert.equal(outcome.calls, 1);
    assert.equal(outcome.versions, 1);
    assert.equal(outcome.active, true);
    assert.equal(outcome.starting, false);
    return outcome;
  });
} finally {
  const passed = results.length === 6 && results.every(result => result.passed) && errors.length === 0;
  await writeFile(path.join(root, 'artifacts', 'edge-cases.json'), JSON.stringify({ passed, results, errors, profile, note: 'Actual offline Electron UI and AudioContext; short controlled charts reduce waiting. Keyboard checks use Playwright keyboard events.' }, null, 2));
  console.log(JSON.stringify({ passed, passedCases: results.filter(result => result.passed).length, cases: results.length, errors }, null, 2));
  await desktop.close();
  if (!passed) process.exitCode = 1;
}
