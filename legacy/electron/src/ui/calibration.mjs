import { mapOutputTimestamp } from '../core/audio.mjs';

const BEAT_SEC = .5;
const PREP_BEATS = 4;
const TAP_BEATS = 32;
const MAX_ERROR_SEC = .22;
const median = values => {
  if (!values.length) return 0;
  const sorted = [...values].sort((a, b) => a - b), middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
};
const signedMs = us => `${us > 0 ? '+' : ''}${(us / 1000).toFixed(2)}`;

/** A tap estimate includes human timing: it is not a hardware latency measurement. */
export function summarizeCalibration(errorsUs) {
  const values = errorsUs.filter(Number.isFinite);
  const center = median(values);
  const mean = values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : 0;
  const madUs = median(values.map(value => Math.abs(value - center)));
  const stdUs = values.length ? Math.sqrt(values.reduce((sum, value) => sum + (value - mean) ** 2, 0) / values.length) : 0;
  const deviations = values.map(value => Math.abs(value - center)).sort((a, b) => a - b);
  const p95Us = deviations.length ? deviations[Math.max(0, Math.ceil(deviations.length * .95) - 1)] : 0;
  return { count: values.length, recommendedUs: Math.round(center), medianUs: center, madUs, stdUs, p95Us,
    canApply: values.length >= 24,
    reliable: values.length >= 24 && madUs <= 20_000 && stdUs <= 40_000 && p95Us <= 70_000 };
}

export function performanceEventTime(timeStamp, timeOrigin = globalThis.performance?.timeOrigin ?? 0, fallback = globalThis.performance?.now() ?? 0) {
  if (!Number.isFinite(timeStamp) || timeStamp < 0) return fallback;
  return timeStamp > 1e12 ? timeStamp - timeOrigin : timeStamp;
}

export function calibrationAudioTime(context, performanceTimeMs, nowMs = globalThis.performance.now()) {
  if (typeof context.getOutputTimestamp === 'function') {
    const timestamp = context.getOutputTimestamp();
    if (Number.isFinite(timestamp.contextTime) && timestamp.contextTime > 0 && Number.isFinite(timestamp.performanceTime) && timestamp.performanceTime > 0) {
      return { time: mapOutputTimestamp(timestamp, performanceTimeMs), source: '출력 타임스탬프' };
    }
  }
  // Same fallback as AudioSystem: each latency component is subtracted once.
  const latency = (context.baseLatency || 0) + (context.outputLatency || 0);
  return { time: context.currentTime - latency + (performanceTimeMs - nowMs) / 1000, source: '출력 지연 추정' };
}

export function pickCalibrationBeat(targets, timeSec, used, maxErrorSec = MAX_ERROR_SEC) {
  let chosen = -1, distance = Infinity;
  targets.forEach((target, index) => {
    const candidate = Math.abs(timeSec - target);
    if (!used.has(index) && candidate <= maxErrorSec && candidate < distance) { chosen = index; distance = candidate; }
  });
  return chosen;
}

function installStyle(document) {
  if (document.getElementById('muworld-calibration-style')) return;
  const style = document.createElement('style');
  style.id = 'muworld-calibration-style';
  style.textContent = `.calibration-widget .cal-description{margin:0}.calibration-widget .calibration-zone{height:155px;margin:18px 0}.calibration-widget .calibration-zone strong{font-size:30px}.calibration-widget .calibration-zone .cal-count{font-variant-numeric:tabular-nums}.calibration-widget .metric{padding:15px 13px}.calibration-widget .metric-value{font-size:25px;margin:9px 0 5px}.calibration-widget .metric-note{line-height:1.6}.calibration-widget .cal-recommendation{border:1px solid var(--line);border-radius:9px;padding:15px 17px;margin-top:16px;background:#202a2b}.calibration-widget .cal-recommendation p{font-size:12px;line-height:1.8;margin:5px 0 0}.calibration-widget .cal-recommendation strong{font-weight:500;font-size:14px;color:var(--accent)}.calibration-widget .cal-environment{font-size:10px;line-height:1.8;color:var(--muted);margin-top:14px}.calibration-widget .cal-tap-hint{color:var(--muted);font-size:10px;margin-top:8px}.calibration-widget .cal-progress{height:3px;position:absolute;bottom:0;left:0;right:0;background:#29343e;overflow:hidden;border-radius:0 0 10px 10px}.calibration-widget .cal-progress i{height:100%;display:block;background:var(--accent);width:0}.calibration-widget .cal-actions{display:flex;gap:9px;justify-content:flex-end;margin-top:20px}.calibration-widget .cal-trust-low{color:#e6c18f!important}.calibration-widget .cal-physical-note{font-size:10px;line-height:1.8;color:var(--muted);margin-top:13px}@media(max-width:650px){.calibration-widget .grid-3{gap:8px}.calibration-widget .metric{padding:12px 9px}.calibration-widget .metric-value{font-size:20px}.calibration-widget .cal-actions{flex-wrap:wrap}}`;
  document.head.append(style);
}

export function mountCalibration(container, { audio, settings, onSave, onClose, toast = () => {} }) {
  const document = container.ownerDocument, window = document.defaultView;
  installStyle(document);
  container.classList.add('calibration-widget');
  container.innerHTML = `
    <div class="modal-top"><h2>내 리듬에 맞추기</h2><button type="button" class="btn icon-btn" data-cal="close" aria-label="보정 닫기">✕</button></div>
    <p class="cal-description">준비 소리 4번 뒤, 일정한 클릭 소리에 맞춰 <kbd class="key">SPACE</kbd>를 32번 눌러 주세요. 화면보다 <strong>들리는 소리</strong>에 집중해 주세요.</p>
    <div class="calibration-zone" tabindex="-1" data-cal="zone"><span class="cal-count" data-cal="count">120 BPM · 약 18초</span><strong data-cal="status">준비되셨나요?</strong><p data-cal="detail">현재 보정값 ${signedMs(settings.inputOffsetUs || 0)} ms</p><div class="cal-progress"><i data-cal="progress"></i></div></div>
    <div class="grid-3">
      <div class="metric"><div class="metric-label">유효한 탭</div><div class="metric-value" data-cal="valid">0<small>/ 32</small></div><div class="metric-note">적용하려면 24회 이상</div></div>
      <div class="metric"><div class="metric-label">중앙값 추천</div><div class="metric-value" data-cal="median">—<small>ms</small></div><div class="metric-note">현재 보정 전 입력 오차</div></div>
      <div class="metric"><div class="metric-label">타이밍 산포 · MAD</div><div class="metric-value" data-cal="mad">—<small>ms</small></div><div class="metric-note" data-cal="spread">일정하게 누를수록 작아져요</div></div>
    </div>
    <div class="cal-recommendation" aria-live="polite"><strong data-cal="trust">직접 확인한 뒤 적용해 주세요</strong><p data-cal="recommend">추천값은 자동으로 저장되지 않습니다. 양수 보정값은 입력을 그만큼 앞당겨 평가합니다.</p></div>
    <div class="cal-environment" data-cal="environment"></div>
    <div class="cal-physical-note">이 검사는 사용자의 누르는 습관을 포함한 <strong>체감 보정</strong>입니다. 하드웨어 지연을 물리적으로 측정하거나 0 ms 오차를 보장하지 않습니다. ±220 ms 밖의 탭과 같은 박자의 추가 탭은 제외합니다.</div>
    <div class="cal-actions"><button type="button" class="btn" data-cal="cancel" hidden>측정 중단</button><button type="button" class="btn" data-cal="start">소리 듣고 시작</button><button type="button" class="btn primary" data-cal="apply" disabled>추천값 적용</button></div>`;
  const refs = Object.fromEntries([...container.querySelectorAll('[data-cal]')].map(element => [element.dataset.cal, element]));
  let destroyed = false, running = false, starting = false, spaceDown = false, generation = 0, frame = 0;
  let context = null, master = null, startSec = 0, targets = [], errors = [], used = new Set(), result = null, clockSource = '측정 전';
  const oscillators = new Set();
  const setText = (name, value) => { if (refs[name].textContent !== value) refs[name].textContent = value; };
  const environment = () => {
    const info = audio.getInfo?.() ?? {};
    const sample = context?.sampleRate || info.sampleRate;
    const latency = context && Number.isFinite(context.outputLatency) ? `${(context.outputLatency * 1000).toFixed(2)} ms` : '제공 안 됨';
    setText('environment', `${sample ? `${(sample / 1000).toFixed(1)} kHz` : '오디오 준비 전'} · 출력 지연 추정 ${latency} · 시계 ${clockSource}`);
  };
  const cancelAudio = () => {
    if (frame) window.cancelAnimationFrame(frame);
    frame = 0;
    for (const oscillator of oscillators) { try { oscillator.stop(); } catch {} try { oscillator.disconnect(); } catch {} }
    oscillators.clear();
    if (master) { try { master.disconnect(); } catch {} master = null; }
    context?.removeEventListener('statechange', contextState);
  };
  const stop = (message = '') => {
    generation++;
    running = false;
    starting = false;
    spaceDown = false;
    cancelAudio();
    refs.start.disabled = false;
    refs.start.textContent = '다시 측정';
    refs.cancel.hidden = true;
    if (message) { setText('status', '측정이 중단됐어요'); setText('detail', message); }
  };
  const drawMetrics = () => {
    result = summarizeCalibration(errors);
    refs.valid.innerHTML = `${result.count}<small>/ 32</small>`;
    refs.median.innerHTML = `${result.count ? signedMs(result.recommendedUs) : '—'}<small>ms</small>`;
    refs.mad.innerHTML = `${result.count ? (result.madUs / 1000).toFixed(2) : '—'}<small>ms</small>`;
    setText('spread', result.count ? `표준편차 ${(result.stdUs / 1000).toFixed(2)} · 편차 p95 ${(result.p95Us / 1000).toFixed(2)} ms` : '일정하게 누를수록 작아져요');
    refs.progress.style.width = `${used.size / TAP_BEATS * 100}%`;
  };
  const complete = () => {
    stop();
    drawMetrics();
    setText('status', '측정 완료');
    setText('detail', `${result.count}회의 타이밍으로 계산했어요`);
    refs.apply.disabled = !result.canApply;
    refs.trust.classList.toggle('cal-trust-low', !result.reliable);
    if (!result.canApply) {
      setText('trust', '유효한 탭이 부족해요');
      setText('recommend', '클릭 소리마다 한 번씩 눌러 24회 이상 기록해 주세요. 현재 설정은 그대로 유지됩니다.');
    } else {
      setText('trust', result.reliable ? `추천 보정값 ${signedMs(result.recommendedUs)} ms` : '산포가 커서 추천 신뢰도가 낮아요');
      setText('recommend', result.reliable ? '중앙값을 기준으로 계산했습니다. 추천값 적용을 눌러야 저장됩니다.' : `추천값은 ${signedMs(result.recommendedUs)} ms입니다. 소리에 집중해 다시 측정하는 것을 권합니다. 적용 여부는 직접 선택해 주세요.`);
    }
    environment();
  };
  function contextState() { if (running && context.state !== 'running') stop('오디오 상태가 바뀌었습니다. 다시 시작해 주세요.'); }
  const tick = () => {
    if (!running || destroyed) return;
    try {
      const mapped = calibrationAudioTime(context, window.performance.now());
      clockSource = mapped.source;
      const beat = Math.floor((mapped.time - startSec) / BEAT_SEC);
      if (beat < 0) { setText('status', '귀를 기울여 주세요'); setText('detail', '곧 준비 소리 4번이 들려요'); }
      else if (beat < PREP_BEATS) { setText('status', `${PREP_BEATS - beat}`); setText('detail', '준비 박자 · 아직 누르지 마세요'); }
      else { setText('status', 'SPACE'); setText('detail', '일정한 소리에 맞춰 눌러 주세요'); }
      setText('count', beat < PREP_BEATS ? '준비 4박 · 120 BPM' : `${Math.min(TAP_BEATS, beat - PREP_BEATS + 1)} / 32박 · 기록 ${used.size}회`);
      if (mapped.time > targets.at(-1) + MAX_ERROR_SEC + .08) { complete(); return; }
      frame = window.requestAnimationFrame(tick);
    } catch { stop('오디오 시각을 읽지 못했습니다. 다시 시작해 주세요.'); }
  };
  const start = async () => {
    if (destroyed || starting) return;
    stop();
    const token = ++generation;
    starting = true;
    refs.start.disabled = true;
    refs.apply.disabled = true;
    setText('status', '소리 준비 중');
    try {
      audio.stop?.();
      await audio.init();
      if (destroyed || token !== generation) return;
      context = audio.context;
      if (!context || context.state !== 'running') throw new Error('오디오 장치를 시작하지 못했습니다.');
      context.addEventListener('statechange', contextState);
      errors = []; used = new Set(); result = null;
      startSec = context.currentTime + .35;
      targets = Array.from({ length: TAP_BEATS }, (_, index) => startSec + (PREP_BEATS + index) * BEAT_SEC);
      master = context.createGain();
      master.gain.value = Math.min(1, Math.max(.2, Number(settings.volume) || .7)) * .19;
      master.connect(context.destination);
      for (let index = 0; index < PREP_BEATS + TAP_BEATS; index++) {
        const oscillator = context.createOscillator(), envelope = context.createGain(), at = startSec + index * BEAT_SEC;
        oscillator.frequency.value = index < PREP_BEATS ? 660 : index % 4 === 0 ? 1050 : 880;
        envelope.gain.setValueAtTime(.0001, at);
        envelope.gain.exponentialRampToValueAtTime(1, at + .0015);
        envelope.gain.exponentialRampToValueAtTime(.0001, at + .035);
        oscillator.connect(envelope).connect(master);
        oscillators.add(oscillator);
        oscillator.onended = () => { oscillators.delete(oscillator); oscillator.disconnect(); envelope.disconnect(); };
        oscillator.start(at); oscillator.stop(at + .045);
      }
      starting = false;
      running = true;
      refs.cancel.hidden = false;
      refs.start.textContent = '측정 중…';
      refs.trust.classList.remove('cal-trust-low');
      setText('trust', '소리에 맞춰 편하게 눌러 주세요');
      setText('recommend', '현재 보정값을 적용하지 않은 원래 입력 오차를 기록합니다. 측정 중에는 설정을 바꾸지 않습니다.');
      drawMetrics(); environment();
      refs.zone.focus({ preventScroll: true });
      frame = window.requestAnimationFrame(tick);
    } catch (error) {
      if (destroyed || token !== generation) return;
      stop('소리를 준비하지 못했습니다. 다시 시작해 주세요.');
      toast(error?.message || '오디오 장치를 확인해 주세요.');
    }
  };
  const close = () => { destroy(); onClose?.(); };
  const keydown = event => {
    if (event.code === 'Escape') { event.preventDefault(); event.stopImmediatePropagation(); close(); return; }
    if (event.code !== 'Space' || !running) return;
    event.preventDefault(); event.stopImmediatePropagation();
    if (event.repeat || spaceDown) return;
    spaceDown = true;
    try {
      const eventMs = performanceEventTime(event.timeStamp, window.performance.timeOrigin, window.performance.now());
      const mapped = calibrationAudioTime(context, eventMs);
      clockSource = mapped.source;
      const index = pickCalibrationBeat(targets, mapped.time, used);
      if (index < 0) return;
      used.add(index);
      errors.push((mapped.time - targets[index]) * 1_000_000);
      drawMetrics();
    } catch { stop('입력 시각을 계산하지 못했습니다. 다시 시작해 주세요.'); }
  };
  const keyup = event => { if (event.code === 'Space') { spaceDown = false; if (running) { event.preventDefault(); event.stopImmediatePropagation(); } } };
  const blur = () => { if (running || starting) stop('창을 벗어나 측정을 중단했습니다. 다시 시작해 주세요.'); };
  const visibility = () => { if (document.hidden) blur(); };
  const click = async event => {
    const action = event.target.closest('[data-cal]')?.dataset.cal;
    if (action === 'start') start();
    else if (action === 'close') close();
    else if (action === 'cancel') stop('측정을 직접 중단했습니다. 다시 시작할 수 있어요.');
    else if (action === 'apply' && !running && result?.canApply) {
      refs.apply.disabled = true;
      try { await onSave?.(result.recommendedUs); if (!destroyed) { toast(`입력 보정을 ${signedMs(result.recommendedUs)} ms로 적용했습니다.`); close(); } }
      catch (error) { if (!destroyed) { refs.apply.disabled = false; toast(error?.message || '보정값을 저장하지 못했습니다.'); } }
    }
  };
  function destroy() {
    if (destroyed) return;
    destroyed = true;
    stop();
    container.removeEventListener('click', click);
    window.removeEventListener('keydown', keydown, true);
    window.removeEventListener('keyup', keyup, true);
    window.removeEventListener('blur', blur);
    document.removeEventListener('visibilitychange', visibility);
  }
  container.addEventListener('click', click);
  window.addEventListener('keydown', keydown, true);
  window.addEventListener('keyup', keyup, true);
  window.addEventListener('blur', blur);
  document.addEventListener('visibilitychange', visibility);
  environment();
  return { destroy };
}
