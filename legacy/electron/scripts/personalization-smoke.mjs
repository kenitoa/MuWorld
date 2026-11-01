import { _electron as electron } from 'playwright';
import { mkdir, mkdtemp, writeFile } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';

const root = process.cwd();
await mkdir('artifacts', { recursive: true });
const profile = await mkdtemp(path.join(root, 'artifacts', 'personalization-profile-'));
const env = { ...process.env, MUWORLD_TEST: '1', MUWORLD_DATA_DIR: profile };
delete env.ELECTRON_RUN_AS_NODE;
const report = { passed: false, profile, startedAt: new Date().toISOString(), checks: [], errors: [] };
let desktop, page, currentCheck = 'launch';
const check = async (name, callback) => {
  currentCheck = name;
  const details = await callback();
  report.checks.push({ name, passed: true, ...(details === undefined ? {} : { details }) });
};

try {
  desktop = await electron.launch({ args: ['.'], cwd: root, env, timeout: 60_000 });
  page = await desktop.firstWindow();
  page.on('pageerror', error => report.errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') report.errors.push(message.text()); });
  await page.waitForSelector('.library-layout');
  await page.waitForFunction(() => Boolean(globalThis.__muworld));

  await check('native-profile-save-apply-close-preserves-5000us', async () => {
    await page.locator('.nav-item[data-route="settings"]').click();
    const offset = page.locator('[data-offset="inputOffsetUs"]');
    await offset.fill('5'); await offset.blur();
    assert.equal(await page.evaluate(() => __muworld.settings.inputOffsetUs), 5000);
    await page.locator('[data-action="profiles"]').click();
    await page.locator('[data-profile="name"]').fill('Desktop QA profile');
    await page.locator('#profiles-mount button[type="submit"]').click();
    const stored = await page.evaluate(() => JSON.parse((globalThis.muworldStorage ?? localStorage).getItem('muworld:profiles')));
    assert.equal(stored.version, 1); assert.equal(stored.profiles.length, 1);
    assert.equal(stored.profiles[0].inputOffsetUs, 5000);
    await page.locator('[data-profile="close"]').click();
    assert.equal(await page.locator('#modal-root').locator('*').count(), 0);
    await page.locator('[data-offset="inputOffsetUs"]').fill('7');
    await page.locator('[data-offset="inputOffsetUs"]').blur();
    assert.equal(await page.evaluate(() => __muworld.settings.inputOffsetUs), 7000);
    await page.locator('[data-action="profiles"]').click();
    await page.locator('[data-profile-action="apply"]').click();
    await page.waitForFunction(() => __muworld.settings.inputOffsetUs === 5000);
    await page.locator('[data-profile="close"]').click();
    assert.equal(await page.locator('[data-offset="inputOffsetUs"]').inputValue(), '5');
    assert.equal(await page.evaluate(() => __muworld.modalController), null);
    return { savedUs: stored.profiles[0].inputOffsetUs, appliedUs: 5000 };
  });

  await check('complete-backup-includes-profile-and-exact-offset', async () => {
    const backup = await page.evaluate(() => JSON.parse(__muworld.backupText()));
    assert.equal(backup.settings.inputOffsetUs, 5000);
    assert.equal(backup.calibrationProfiles.version, 1);
    assert.equal(backup.calibrationProfiles.profiles[0].inputOffsetUs, 5000);
    return { profileCount: backup.calibrationProfiles.profiles.length };
  });

  await check('precision-window-quarter-scale-selected-and-persisted', async () => {
    await page.locator('.nav-item[data-route="practice"]').click();
    await page.locator('#practice-window').selectOption('0.25');
    assert.equal(await page.evaluate(() => __muworld.settings.windowScale), 0.25);
    const persisted = await page.evaluate(() => JSON.parse((globalThis.muworldStorage ?? localStorage).getItem('muworld:v1')).settings.windowScale);
    assert.equal(persisted, 0.25);
    await page.locator('.nav-item[data-route="settings"]').click();
    await page.locator('.nav-item[data-route="practice"]').click();
    assert.equal(await page.locator('#practice-window').inputValue(), '0.25');
    return { windowScale: persisted, perfectWindowMs: 5 };
  });

  await check('tutorial-actual-timed-step-navigation-and-finish', async () => {
    await page.locator('[data-action="tutorial"]').click();
    await page.waitForSelector('#tutorial-mount .tutorial-zone');
    assert.equal(await page.locator('.tutorial-step.active').getAttribute('data-step'), '0');
    await page.locator('[data-tutorial="start"]').click();
    await page.waitForTimeout(1450); await page.keyboard.press('d');
    await page.waitForFunction(() => document.querySelector('[data-tutorial="progress"]')?.style.width === '100%', null, { timeout: 15_000 });
    assert.ok((await page.locator('[data-tutorial="feedback"]').textContent()).includes('MISS'));
    await page.locator('[data-tutorial="next"]').click();
    assert.equal(await page.locator('.tutorial-step.active').getAttribute('data-step'), '1');
    await page.locator('[data-tutorial="previous"]').click();
    assert.equal(await page.locator('.tutorial-step.active').getAttribute('data-step'), '0');
    await page.locator('[data-step="1"]').click();
    await page.locator('[data-step="2"]').click();
    await page.locator('[data-tutorial="start"]').click();
    await page.waitForTimeout(250);
    await page.locator('[data-step="3"]').click();
    assert.equal(await page.locator('.tutorial-step.active').getAttribute('data-step'), '3');
    await page.locator('[data-tutorial="next"]').click();
    assert.equal(await page.locator('#tutorial-mount').count(), 0);
    assert.equal(await page.evaluate(() => __muworld.modalController), null);
    await page.keyboard.press('d'); await page.keyboard.press('j');
    assert.equal(await page.evaluate(() => __muworld.route), 'practice');
    assert.equal(await page.evaluate(() => __muworld.settings.inputOffsetUs), 5000);
    return { timedStage: 1, navigatedStages: 4, closed: true };
  });

  for (const kind of ['echo', 'silence', 'workshop', 'guard', 'route']) {
    await check(`arcade-${kind}-start-and-navigation-destroy`, async () => {
      await page.locator('.nav-item[data-route="arcade"]').click();
      await page.locator(`[data-action="arcade-start"][data-kind="${kind}"]`).click();
      await page.waitForSelector('#arcade-mount .arcade-play');
      assert.ok(await page.evaluate(() => Boolean(__muworld.arcadeController)));
      await page.locator('#arcade-mount [data-action="start"]').click();
      await page.waitForSelector('.arcade-pad');
      await page.waitForTimeout(250);
      await page.keyboard.press('d'); await page.keyboard.press('j');
      await page.locator('.nav-item[data-route="library"]').click();
      await page.waitForSelector('.library-layout');
      assert.equal(await page.evaluate(() => __muworld.arcadeController), null);
      await page.keyboard.press('Escape'); await page.keyboard.press('d'); await page.keyboard.press('j');
      await page.waitForTimeout(150);
      assert.equal(await page.evaluate(() => __muworld.route), 'library');
      assert.equal(await page.locator('.arcade-play').count(), 0);
      assert.equal(await page.evaluate(() => __muworld.settings.inputOffsetUs), 5000);
      return { destroyed: true, laterKeysDoNotNavigate: true };
    });
  }

  await check('preview-stops-on-navigation-without-stale-key-handlers', async () => {
    await page.locator('.track-panel [data-action="preview"]').click();
    await page.waitForFunction(() => Boolean(__muworld.previewing));
    await page.locator('.nav-item[data-route="settings"]').click();
    assert.equal(await page.evaluate(() => __muworld.previewing), null);
    await page.keyboard.press('Escape'); await page.keyboard.press('d'); await page.keyboard.press('j');
    assert.equal(await page.evaluate(() => __muworld.route), 'settings');
    assert.equal(await page.locator('[data-action="profiles"]').count(), 1);
  });

  await check('precision-game-engine-and-return-do-not-leave-session-handlers', async () => {
    await page.evaluate(() => {
      __muworld.selected = 'glasshouse'; __muworld.difficulty = 'easy';
      return __muworld.startGame({ mode: 'practice', rate: 1, fromUs: 4_000_000, toUs: 9_000_000 });
    });
    await page.waitForSelector('#game-canvas');
    const actual = await page.evaluate(() => ({ windowScale: __muworld.session.engine.windowScale, inputOffsetUs: __muworld.session.engine.inputOffsetUs }));
    assert.deepEqual(actual, { windowScale: 0.25, inputOffsetUs: 5000 });
    await page.keyboard.press('Escape');
    await page.waitForSelector('[data-action="quit-game"]');
    await page.locator('[data-action="quit-game"]').click();
    await page.waitForSelector('.library-layout');
    assert.equal(await page.evaluate(() => __muworld.session), null);
    await page.locator('.nav-item[data-route="records"]').click();
    await page.keyboard.press('d'); await page.keyboard.press('j'); await page.keyboard.press('Escape');
    assert.equal(await page.evaluate(() => __muworld.route), 'records');
    assert.equal(await page.locator('.award').count(), 5);
    assert.equal(await page.locator('.award.earned').count(), 0, 'interrupted practice and unfinished arcade must not fabricate achievements');
    return actual;
  });

  await check('native-settings-and-profile-survive-reload', async () => {
    await page.reload(); await page.waitForSelector('.library-layout');
    assert.equal(await page.evaluate(() => __muworld.settings.inputOffsetUs), 5000);
    assert.equal(await page.evaluate(() => __muworld.settings.windowScale), 0.25);
    const profiles = await page.evaluate(() => JSON.parse((globalThis.muworldStorage ?? localStorage).getItem('muworld:profiles')).profiles);
    assert.equal(profiles.length, 1); assert.equal(profiles[0].inputOffsetUs, 5000);
    return { inputOffsetUs: 5000, windowScale: 0.25, profileCount: profiles.length };
  });

  assert.deepEqual(report.errors, []);
  report.passed = true;
  console.log(JSON.stringify({ passed: true, checks: report.checks.length, errors: report.errors }, null, 2));
} catch (error) {
  report.failedCheck = currentCheck;
  report.failure = { message: error.message, stack: error.stack };
  console.error(JSON.stringify({ passed: false, failedCheck: currentCheck, message: error.message, errors: report.errors }, null, 2));
  process.exitCode = 1;
} finally {
  report.finishedAt = new Date().toISOString();
  await writeFile(path.join(root, 'artifacts', 'personalization-smoke.json'), JSON.stringify(report, null, 2));
  await desktop?.close();
}
