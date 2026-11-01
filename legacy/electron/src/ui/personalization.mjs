import { RhythmEngine } from '../core/judge.mjs';
import { mapOutputTimestamp } from '../core/audio.mjs';

const PROFILE_KEY = 'muworld:profiles';
const BEAT_US = 600_000;
const esc = value => String(value).replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character]));
const label = code => String(code).replace(/^Key/, '').replace(/^Digit/, '').replace(/^Arrow/, '');
const finite = value => typeof value === 'number' && Number.isFinite(value);
const signedMs = value => `${value > 0 ? '+' : ''}${Number((value / 1000).toFixed(3))} ms`;

/** Awards are derived from saved, completed performances; no seeded achievements. */
export function getAwards(records = []) {
  const complete = (Array.isArray(records) ? records : []).filter(record => record && record.completed === true && record.interrupted === false);
  const standard = complete.filter(record => record.mode === 'standard');
  const bestAccuracy = standard.reduce((best, record) => Math.max(best, finite(record.summary?.accuracy) ? record.summary.accuracy : 0), 0);
  const bestCombo = standard.reduce((best, record) => Math.max(best, Number.isSafeInteger(record.summary?.maxCombo) ? record.summary.maxCombo : 0), 0);
  const practiceCount = complete.filter(record => record.mode === 'practice').length;
  const songs = new Set(standard.filter(record => typeof record.songId === 'string' && record.songId).map(record => record.songId)).size;
  const award = (id, title, description, current, target) => ({ id, title, description, earned: current >= target, progress: Math.min(target, Math.max(0, current)), target });
  return [
    award('first-finish', '첫 번째 완주', '정규 곡을 중단 없이 처음부터 끝까지 연주하세요.', standard.length, 1),
    award('accuracy-90', '안정적인 리듬', '정규 곡에서 정확도 90% 이상을 기록하세요.', bestAccuracy, 90),
    award('combo-100', '100콤보의 흐름', '정규 곡에서 최대 콤보 100을 달성하세요.', bestCombo, 100),
    award('practice-3', '꾸준한 연습', '구간 연습을 중단 없이 3회 완료하세요.', practiceCount, 3),
    award('five-songs', '다섯 가지 색', '서로 다른 정규 곡 5곡을 완주하세요.', songs, 5),
  ];
}

function styles(document) {
  if (document.getElementById('muworld-personalization-styles')) return;
  const element = document.createElement('style');
  element.id = 'muworld-personalization-styles';
  element.textContent = `
.personal-widget .personal-description{font-size:12px;line-height:1.9;color:var(--sub);margin:0 0 18px}.personal-widget .personal-muted{font-size:10px;line-height:1.9;color:var(--muted)}.personal-widget .profile-current{display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin:18px 0}.personal-widget .profile-value{border:1px solid var(--line);background:#202936;border-radius:8px;padding:15px}.personal-widget .profile-value small{display:block;color:var(--muted);font-size:10px;margin-bottom:9px}.personal-widget .profile-value strong{font-size:19px;font-weight:450;color:#d6e1cc}.personal-widget .profile-form{display:flex;gap:10px;margin:21px 0}.personal-widget .profile-form input{flex:1;min-width:0;background:#121a24;border:1px solid #424e5d;border-radius:7px;padding:11px 13px;font-size:12px;color:var(--text)}.personal-widget .profile-list{display:flex;flex-direction:column;gap:9px;margin:18px 0;max-height:260px;overflow:auto}.personal-widget .profile-item{border:1px solid #35404d;border-radius:9px;padding:14px 15px;background:#202834;display:flex;gap:15px;align-items:center}.personal-widget .profile-item-copy{flex:1;min-width:0}.personal-widget .profile-item h3{font-size:13px;font-weight:550;margin:0 0 6px;overflow-wrap:anywhere}.personal-widget .profile-item p{font-size:10px;line-height:1.8;color:var(--sub);margin:0}.personal-widget .profile-item-actions{display:flex;gap:6px}.personal-widget .profile-empty{border:1px dashed #3b4654;border-radius:9px;padding:26px;text-align:center;font-size:12px;color:var(--muted);line-height:1.9}.personal-widget .profile-device{display:flex;justify-content:space-between;gap:12px;align-items:center;margin:15px 0}.personal-widget .profile-device p{font-size:10px;color:var(--muted);line-height:1.8;margin:0}.personal-widget .tutorial-steps{display:grid;grid-template-columns:repeat(4,1fr);gap:7px;margin:20px 0}.personal-widget .tutorial-step{min-height:39px;background:#232d39;border:1px solid #384453;border-radius:7px;font-size:11px;color:var(--sub)}.personal-widget .tutorial-step.active{background:#303c2a;border-color:#82996c;color:#d4e5bc}.personal-widget .tutorial-zone{position:relative;background:#141d27;border:1px solid #354252;border-radius:11px;padding:20px;margin-top:15px;text-align:center;overflow:hidden}.personal-widget .tutorial-zone h3{font-size:23px;font-weight:450;letter-spacing:-.5px;margin:5px 0 10px}.personal-widget .tutorial-zone p{font-size:11px;line-height:1.8;color:var(--sub);margin:0;min-height:20px}.personal-widget .tutorial-canvas{height:170px;width:100%;display:block;margin:10px 0 0}.personal-widget .tutorial-feedback{height:26px;font-size:13px;letter-spacing:1px;color:#c3dba4;margin-top:4px}.personal-widget .tutorial-pads{display:flex;gap:10px;max-width:340px;margin:12px auto 0}.personal-widget .tutorial-pad{flex:1;border:1px solid #455368;border-radius:8px;background:#252f3d;min-height:62px;display:flex;align-items:center;justify-content:center;gap:12px;touch-action:none}.personal-widget .tutorial-pad strong{font-size:23px;font-weight:450;color:#c7bedf}.personal-widget .tutorial-pad span{font-size:10px;color:var(--muted)}.personal-widget .tutorial-pad.on{background:#35432f;border-color:#9bb381}.personal-widget .tutorial-pad.on strong{color:#d1e4b9}.personal-widget .tutorial-actions{display:flex;gap:8px;justify-content:space-between;margin-top:20px;align-items:center}.personal-widget .tutorial-actions>div{display:flex;gap:8px}.personal-widget .tutorial-status{font-size:10px;color:var(--muted);min-height:18px;margin-top:12px;line-height:1.8}.personal-widget .tutorial-results{display:flex;justify-content:center;gap:24px;margin:25px 0 18px}.personal-widget .tutorial-results strong{font-size:28px;font-weight:450;display:block;margin-bottom:8px;color:#c8dcb1}.personal-widget .tutorial-results span{font-size:10px;color:var(--muted)}.personal-widget .tutorial-track{height:3px;background:#2b3643;margin-top:12px;border-radius:3px;overflow:hidden}.personal-widget .tutorial-track i{height:100%;display:block;background:#98b07d;width:0}.personal-widget .tutorial-note{margin:15px 0 0;font-size:10px;color:var(--muted);line-height:1.8}@media(max-width:650px){.personal-widget .profile-current{gap:7px}.personal-widget .profile-value{padding:12px}.personal-widget .profile-value strong{font-size:16px}.personal-widget .profile-item{align-items:flex-start;gap:7px}.personal-widget .tutorial-actions{flex-wrap:wrap}}
`;
  document.head.append(element);
}

function validProfile(profile) {
  return profile && typeof profile === 'object' && !Array.isArray(profile)
    && typeof profile.id === 'string' && profile.id.length > 0 && profile.id.length <= 100
    && typeof profile.name === 'string' && profile.name.trim().length > 0 && profile.name.length <= 80
    && Number.isSafeInteger(profile.inputOffsetUs) && Math.abs(profile.inputOffsetUs) <= 500_000
    && Number.isSafeInteger(profile.visualOffsetUs) && Math.abs(profile.visualOffsetUs) <= 500_000
    && Array.isArray(profile.keys) && profile.keys.length === 4 && new Set(profile.keys).size === 4 && profile.keys.every(code => typeof code === 'string' && /^[A-Za-z][A-Za-z0-9]{0,39}$/.test(code) && !['Escape', 'Tab', 'MetaLeft', 'MetaRight'].includes(code))
    && (profile.sampleRate === null || Number.isSafeInteger(profile.sampleRate) && profile.sampleRate >= 8_000 && profile.sampleRate <= 384_000)
    && typeof profile.updatedAt === 'string' && Number.isFinite(Date.parse(profile.updatedAt));
}

export function mountProfiles(container, { audio, settings, onApply = () => {}, onClose = () => {}, toast = () => {} }) {
  const document = container.ownerDocument, window = document.defaultView;
  styles(document); container.classList.add('personal-widget');
  const abort = new AbortController();
  let destroyed = false, checking = false, profiles = [], invalidStored = false, raw = null;
  const captured = { inputOffsetUs: settings.inputOffsetUs ?? 0, visualOffsetUs: settings.visualOffsetUs ?? 0, keys: [...settings.keys] };
  let storage;
  try { storage = globalThis.muworldStorage ?? globalThis.localStorage; } catch { storage = null; }
  try {
    raw = storage?.getItem(PROFILE_KEY) ?? null;
    if (raw) {
      if (raw.length > 150_000) throw new Error('프로필 파일이 너무 큽니다.');
      const parsed = JSON.parse(raw);
      if (parsed.version !== 1 || !Array.isArray(parsed.profiles) || parsed.profiles.length > 30 || !parsed.profiles.every(validProfile) || new Set(parsed.profiles.map(profile => profile.id)).size !== parsed.profiles.length) throw new Error('저장된 프로필 형식을 확인할 수 없습니다.');
      profiles = parsed.profiles;
    }
  } catch { invalidStored = true; }
  container.innerHTML = `<div class="modal-top"><h2>장치별 내 설정</h2><button type="button" class="btn icon-btn" data-profile="close" aria-label="프로필 닫기">×</button></div><p class="personal-description">지금 사용하는 키와 보정값을 이름을 붙여 저장하세요. 예: 유선 헤드폰 · 데스크톱 키보드.</p><div class="profile-current"><div class="profile-value"><small>입력 보정</small><strong data-profile="input">${signedMs(captured.inputOffsetUs)}</strong></div><div class="profile-value"><small>화면 보정</small><strong data-profile="visual">${signedMs(captured.visualOffsetUs)}</strong></div><div class="profile-value"><small>키 배치</small><strong data-profile="keys">${captured.keys.map(code => esc(label(code))).join(' · ')}</strong></div></div><div class="profile-device"><p data-profile="environment"></p><button class="btn small" type="button" data-profile="device">오디오 정보 확인</button></div><form class="profile-form" data-profile="form"><input data-profile="name" aria-label="프로필 이름" placeholder="프로필 이름을 입력하세요" maxlength="40" autocomplete="off" required><button class="btn primary" type="submit">현재 설정 저장</button></form><div class="profile-list" data-profile="list"></div><p class="personal-muted" data-profile="note">프로필 이름은 직접 구분하기 위한 표시입니다. 장치를 자동으로 식별하거나 지연을 측정한 값이 아닙니다. 적용할 때는 키와 두 보정값만 변경합니다.</p>`;
  const refs = Object.fromEntries([...container.querySelectorAll('[data-profile]')].map(element => [element.dataset.profile, element]));
  function environment() {
    const rate = audio.context?.sampleRate;
    refs.environment.textContent = rate ? `현재 오디오 ${Number((rate / 1000).toFixed(1))} kHz · ${audio.context.state === 'running' ? '준비됨' : '대기 중'}` : '오디오가 아직 준비되지 않았습니다. 저장하면 샘플레이트는 미지정으로 남습니다.';
  }
  function render() {
    refs.list.innerHTML = profiles.length ? profiles.map(profile => `<article class="profile-item"><div class="profile-item-copy"><h3>${esc(profile.name)}</h3><p>입력 ${signedMs(profile.inputOffsetUs)} · 화면 ${signedMs(profile.visualOffsetUs)}<br>${profile.keys.map(code => esc(label(code))).join(' · ')} · ${profile.sampleRate ? `${Number((profile.sampleRate / 1000).toFixed(1))} kHz` : '오디오 정보 미지정'}</p></div><div class="profile-item-actions"><button type="button" class="btn small" data-profile-action="apply" data-id="${esc(profile.id)}" aria-label="${esc(profile.name)} 적용">적용</button><button type="button" class="btn small ghost" data-profile-action="delete" data-id="${esc(profile.id)}" aria-label="${esc(profile.name)} 삭제">삭제</button></div></article>`).join('') : '<div class="profile-empty">아직 저장한 프로필이 없어요.<br>현재 설정에 이름을 붙여 첫 프로필을 만들어보세요.</div>';
    refs.note.textContent = invalidStored ? '저장된 프로필을 읽지 못했습니다. 새 프로필을 저장하면 이전 원본은 별도 백업으로 보존합니다.' : '프로필 이름은 직접 구분하기 위한 표시입니다. 장치를 자동으로 식별하거나 지연을 측정한 값이 아닙니다. 적용할 때는 키와 두 보정값만 변경합니다.';
  }
  function commit(next) {
    if (!storage) throw new Error('이 환경에서는 프로필을 저장할 수 없습니다.');
    if (invalidStored && raw) storage.setItem(`${PROFILE_KEY}:backup`, raw);
    const serialized = JSON.stringify({ version: 1, profiles: next });
    storage.setItem(PROFILE_KEY, serialized);
    profiles = next; invalidStored = false; raw = serialized; render();
  }
  refs.form.addEventListener('submit', event => {
    event.preventDefault();
    const name = refs.name.value.trim();
    if (!name) { refs.name.setCustomValidity('프로필 이름을 입력하세요.'); refs.name.reportValidity(); return; }
    refs.name.setCustomValidity('');
    try {
      if (profiles.length >= 30) throw new Error('프로필은 30개까지 저장할 수 있습니다.');
      const profile = { id: globalThis.crypto?.randomUUID?.() ?? `profile-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`, name, ...captured, keys: [...captured.keys], sampleRate: audio.context?.sampleRate ?? null, updatedAt: new Date().toISOString() };
      if (!validProfile(profile)) throw new Error('현재 키 또는 보정 설정이 유효하지 않습니다.');
      commit([...profiles, profile]); refs.name.value = ''; toast('현재 설정을 프로필로 저장했습니다.');
    } catch (error) { toast(error.message || '프로필을 저장하지 못했습니다.'); }
  }, { signal: abort.signal });
  refs.name.addEventListener('input', () => refs.name.setCustomValidity(''), { signal: abort.signal });
  container.addEventListener('click', async event => {
    const control = event.target.closest('button'); if (!control) return;
    if (control.dataset.profile === 'close') { onClose(); return; }
    if (control.dataset.profile === 'device' && !checking) {
      checking = true; control.disabled = true;
      try { await audio.init(); if (!destroyed) environment(); } catch (error) { if (!destroyed) toast(error.message || '오디오 정보를 확인하지 못했습니다.'); }
      finally { checking = false; if (!destroyed) control.disabled = false; }
      return;
    }
    const profile = profiles.find(item => item.id === control.dataset.id); if (!profile) return;
    try {
      if (control.dataset.profileAction === 'delete') { commit(profiles.filter(item => item.id !== profile.id)); toast('프로필을 삭제했습니다.'); }
      if (control.dataset.profileAction === 'apply') {
        control.disabled = true;
        await onApply({ inputOffsetUs: profile.inputOffsetUs, visualOffsetUs: profile.visualOffsetUs, keys: [...profile.keys] });
        if (!destroyed) {
          Object.assign(captured, { inputOffsetUs: profile.inputOffsetUs, visualOffsetUs: profile.visualOffsetUs, keys: [...profile.keys] });
          refs.input.textContent = signedMs(captured.inputOffsetUs); refs.visual.textContent = signedMs(captured.visualOffsetUs); refs.keys.textContent = captured.keys.map(label).join(' · ');
          toast(`${profile.name} 설정을 적용했습니다.`); control.disabled = false;
        }
      }
    } catch (error) { if (!destroyed) { control.disabled = false; toast(error.message || '프로필을 적용하지 못했습니다.'); } }
  }, { signal: abort.signal });
  window.addEventListener('keydown', event => { if (event.code === 'Escape') { event.preventDefault(); onClose(); } }, { signal: abort.signal });
  environment(); render();
  return { destroy() { if (destroyed) return; destroyed = true; abort.abort(); container.classList.remove('personal-widget'); } };
}

const STEPS = [
  { name: '한 키 탭', description: '노트가 선에 닿을 때 왼쪽 키를 한 번 눌렀다 놓으세요.', lanes: [0] },
  { name: '좌우 교대', description: '왼쪽과 오른쪽 노트를 번갈아 눌러보세요. 누른 뒤에는 키를 놓아주세요.', lanes: [0, 2] },
  { name: '동시치기', description: '같은 높이의 두 노트는 왼쪽과 오른쪽 키를 함께 누르세요.', lanes: [0, 2] },
  { name: '길게 누르기', description: '긴 노트는 머리가 닿을 때 누르고, 연결된 막대가 끝날 때까지 유지하세요.', lanes: [0, 2] },
];
function tutorialChart(step) {
  const notes = [];
  const add = (lane, beat, endBeat) => notes.push({ id: `tutorial-${step}-${notes.length}`, lane, timeUs: beat * BEAT_US, ...(endBeat ? { endUs: endBeat * BEAT_US } : {}), pattern: STEPS[step].name });
  if (step < 2) for (let beat = 2; beat <= 7; beat++) add(step ? beat % 2 ? 2 : 0 : 0, beat);
  else if (step === 2) for (const beat of [2, 4, 6]) { add(0, beat); add(2, beat); }
  else { add(0, 2, 4); add(2, 5, 7); }
  return { id: `tutorial-${step}`, version: '1', durationUs: 8 * BEAT_US, notes, bpm: 100 };
}

export function mountTutorial(container, { audio, settings, onClose = () => {}, toast = () => {} }) {
  const document = container.ownerDocument, window = document.defaultView;
  styles(document); container.classList.add('personal-widget');
  const abort = new AbortController(), oscillators = new Set(), held = new Set();
  const keys = [...settings.keys];
  let destroyed = false, step = 0, state = 'ready', generation = 0, frame = 0, context = null, master = null, observedContext = null;
  let chart = tutorialChart(0), engine = null, startSec = 0, feedback = null, lastJudgment = null;
  let title, detail, canvas, feedbackElement, progress, status, startButton, pads = [];
  container.innerHTML = `<div class="modal-top"><h2>처음 배우는 리듬</h2><button type="button" class="btn icon-btn" data-tutorial="close" aria-label="튜토리얼 닫기">×</button></div><p class="personal-description">짧은 네 단계로 기본 조작을 익혀보세요. 각 단계는 다시 하거나 바로 다음으로 넘어갈 수 있습니다.</p><div class="tutorial-steps">${STEPS.map((item, index) => `<button type="button" class="tutorial-step" data-step="${index}">${index + 1}. ${item.name}</button>`).join('')}</div><div data-tutorial="content"></div><p class="tutorial-note">튜토리얼은 기록·최고 점수에 포함되지 않습니다. 현재 키 배치와 입력 보정 ${signedMs(settings.inputOffsetUs || 0)}를 그대로 사용합니다.</p>`;
  const content = container.querySelector('[data-tutorial="content"]');
  function cancel() {
    generation++; cancelAnimationFrame(frame); held.clear();
    for (const oscillator of oscillators) { try { oscillator.stop(); oscillator.disconnect(); } catch {} }
    oscillators.clear();
    if (master) { try { master.disconnect(); } catch {} master = null; }
  }
  function ready(message = '') {
    state = 'ready'; chart = tutorialChart(step);
    container.querySelectorAll('[data-step]').forEach(button => button.classList.toggle('active', Number(button.dataset.step) === step));
    content.innerHTML = `<div class="tutorial-zone"><span class="eyebrow">STEP ${step + 1} / 4 · 100 BPM</span><h3 data-tutorial="title">${STEPS[step].name}</h3><p data-tutorial="detail">${STEPS[step].description}</p><canvas class="tutorial-canvas" aria-label="노트와 판정선"></canvas><div class="tutorial-pads">${STEPS[step].lanes.map(lane => `<button type="button" class="tutorial-pad" data-lane="${lane}" aria-label="${esc(label(keys[lane]))} 입력"><strong>${esc(label(keys[lane]))}</strong><span>${lane === 0 ? 'LEFT' : 'RIGHT'}</span></button>`).join('')}</div><div class="tutorial-feedback" data-tutorial="feedback"></div><div class="tutorial-track"><i data-tutorial="progress"></i></div></div><div class="tutorial-status" data-tutorial="status">${message || '시작하면 준비 소리 2박 뒤에 노트가 내려옵니다.'}</div><div class="tutorial-actions"><button type="button" class="btn small" data-tutorial="previous" ${step ? '' : 'disabled'}>이전 단계</button><div><button type="button" class="btn" data-tutorial="start">연습 시작하기</button><button type="button" class="btn primary" data-tutorial="next">${step === 3 ? '튜토리얼 끝내기' : '다음 단계'}</button></div></div>`;
    title = content.querySelector('[data-tutorial="title"]'); detail = content.querySelector('[data-tutorial="detail"]');
    canvas = content.querySelector('canvas'); feedbackElement = content.querySelector('[data-tutorial="feedback"]'); progress = content.querySelector('[data-tutorial="progress"]'); status = content.querySelector('[data-tutorial="status"]'); startButton = content.querySelector('[data-tutorial="start"]'); pads = [...content.querySelectorAll('[data-lane]')];
    draw(0, true);
  }
  function outputTime(performanceTimeMs = performance.now()) {
    const timestamp = context.getOutputTimestamp?.();
    if (timestamp && timestamp.contextTime > 0 && timestamp.performanceTime > 0 && finite(timestamp.contextTime) && finite(timestamp.performanceTime)) return mapOutputTimestamp(timestamp, performanceTimeMs);
    return context.currentTime - (context.baseLatency || 0) - (context.outputLatency || 0) + (performanceTimeMs - performance.now()) / 1000;
  }
  const timeUs = performanceTimeMs => Math.round((outputTime(performanceTimeMs) - startSec) * 1_000_000);
  function stamp(event) { return finite(event.timeStamp) && event.timeStamp >= 0 ? event.timeStamp > 1e12 ? event.timeStamp - performance.timeOrigin : event.timeStamp : performance.now(); }
  function click(at, accent) {
    const oscillator = context.createOscillator(), envelope = context.createGain();
    oscillator.frequency.value = accent ? 1000 : 760;
    envelope.gain.setValueAtTime(.13, at); envelope.gain.exponentialRampToValueAtTime(.00001, at + .04);
    oscillator.connect(envelope).connect(master); oscillator.start(at); oscillator.stop(at + .05); oscillators.add(oscillator);
    oscillator.onended = () => { oscillators.delete(oscillator); try { oscillator.disconnect(); envelope.disconnect(); } catch {} };
  }
  function draw(nowUs, preview = false) {
    const width = Math.max(240, canvas.getBoundingClientRect().width), height = 170, ratio = window.devicePixelRatio || 1;
    if (canvas.width !== Math.round(width * ratio) || canvas.height !== height * ratio) { canvas.width = Math.round(width * ratio); canvas.height = height * ratio; }
    const ctx = canvas.getContext('2d'); ctx.setTransform(ratio, 0, 0, ratio, 0, 0); ctx.clearRect(0, 0, width, height);
    const lanes = STEPS[step].lanes, laneWidth = Math.min(115, width / (lanes.length + 1)), left = (width - lanes.length * laneWidth) / 2, line = 142;
    for (let index = 0; index < lanes.length; index++) { ctx.fillStyle = '#1d2733'; ctx.fillRect(left + index * laneWidth + 3, 0, laneWidth - 6, 164); }
    ctx.strokeStyle = '#b2c88e'; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(left, line); ctx.lineTo(left + lanes.length * laneWidth, line); ctx.stroke();
    const visualUs = nowUs + (settings.visualOffsetUs || 0), approach = Math.max(900_000, (settings.approachMs || 1200) * 1000);
    const notes = preview ? chart.notes.slice(0, step === 2 ? 2 : 1) : engine.notes;
    for (const note of notes) {
      if (note.state === 'miss' || note.state === 'hit' && !note.endUs || ['complete', 'broken'].includes(note.holdState)) continue;
      const laneIndex = lanes.indexOf(note.lane), x = left + laneIndex * laneWidth + 16;
      const delta = preview ? approach * .55 : note.timeUs - visualUs;
      const endDelta = preview ? delta + ((note.endUs || note.timeUs) - note.timeUs) : (note.endUs ?? note.timeUs) - visualUs;
      if (delta > approach || endDelta < -150_000) continue;
      const y = note.holdState === 'holding' ? line : line - delta / approach * line;
      if (note.endUs) { const top = Math.max(0, line - endDelta / approach * line); ctx.fillStyle = '#53644b'; ctx.fillRect(x + 10, top, laneWidth - 52, Math.max(0, Math.min(line, y) - top)); }
      ctx.fillStyle = note.lane === 0 ? '#b8cf9d' : '#c1b6de'; ctx.fillRect(x, y - 5, laneWidth - 32, 10);
    }
  }
  function updateJudgment(nowUs) {
    const latest = engine.judgments.at(-1);
    if (latest && (latest !== lastJudgment || latest.grade !== lastJudgment?.grade)) { lastJudgment = latest; feedback = { ...latest, shownUs: nowUs }; }
    if (feedback && nowUs - feedback.shownUs < 650_000) { feedbackElement.textContent = feedback.grade === 'MISS' ? 'MISS · 괜찮아요, 다음 박을 들어보세요' : feedback.kind === 'hold' ? '홀드 완료' : feedback.grade; feedbackElement.style.color = feedback.grade === 'MISS' ? '#d4a49c' : '#c5dca9'; }
    else feedbackElement.textContent = '';
  }
  function finish() {
    const summary = engine.finish(); state = 'finished'; cancel();
    title.textContent = summary.accuracy >= 90 ? '좋아요, 다음 동작으로 가볼까요?' : '한 동작씩 익혀가면 돼요';
    detail.textContent = `${STEPS[step].name} · 정확도 ${summary.accuracy.toFixed(1)}% · 최대 콤보 ${summary.maxCombo}`;
    feedbackElement.textContent = `성공 ${summary.judgedUnits - summary.counts.MISS} / ${summary.totalUnits} 판정 · MISS ${summary.counts.MISS}`;
    feedbackElement.style.color = '#c5dca9'; status.textContent = '다시 연습하거나 다음 단계로 넘어갈 수 있습니다.'; startButton.textContent = '다시 연습하기'; progress.style.width = '100%';
    pads.forEach(pad => pad.classList.remove('on'));
  }
  function tick() {
    if (destroyed || state !== 'playing') return;
    const nowUs = timeUs(); engine.advance(nowUs); updateJudgment(nowUs); draw(nowUs);
    pads.forEach(pad => pad.classList.toggle('on', held.has(keys[Number(pad.dataset.lane)])));
    progress.style.width = `${Math.min(100, Math.max(0, nowUs / chart.durationUs * 100))}%`;
    if (nowUs < 0) { title.textContent = '준비 · 클릭 소리를 들어보세요'; status.textContent = '첫 두 박을 듣고 노트가 선에 닿을 때 시작하세요.'; }
    else { title.textContent = STEPS[step].name; status.textContent = '노트가 판정선에 닿을 때 입력합니다. 점수가 낮아도 다음 단계로 이동할 수 있어요.'; }
    if (nowUs >= chart.durationUs + Math.max(0, settings.inputOffsetUs || 0) + 150_000) finish(); else frame = window.requestAnimationFrame(tick);
  }
  async function start() {
    cancel(); const token = generation; state = 'loading'; startButton.disabled = true; startButton.textContent = '오디오 준비 중…';
    try {
      audio.stop?.(); await audio.init(); if (destroyed || token !== generation) return;
      context = audio.context; if (!context) throw new Error('오디오 장치를 준비하지 못했습니다.');
      if (observedContext !== context) { observedContext?.removeEventListener('statechange', contextState); observedContext = context; context.addEventListener('statechange', contextState, { signal: abort.signal }); }
      master = context.createGain(); master.gain.value = Math.min(1, Math.max(0, settings.volume ?? .7)) * .65; master.connect(context.destination);
      chart = tutorialChart(step); engine = new RhythmEngine(chart, { inputOffsetUs: settings.inputOffsetUs || 0, rate: 1, windowScale: 1 });
      lastJudgment = null; feedback = null; startSec = context.currentTime + .15;
      for (let beat = 0; beat < 8; beat++) click(startSec + beat * .6, beat % 4 === 0);
      state = 'playing'; startButton.disabled = false; startButton.textContent = '연습 중단'; frame = window.requestAnimationFrame(tick);
    } catch (error) { if (!destroyed && token === generation) { cancel(); ready(error.message || '오디오를 준비하지 못했습니다.'); toast(error.message || '오디오를 준비하지 못했습니다.'); } }
  }
  function move(next) { cancel(); step = Math.min(3, Math.max(0, next)); ready(); }
  function interrupt() { if (state === 'playing' || state === 'loading') { cancel(); ready('화면을 벗어나 연습을 멈췄습니다. 다시 시작할 수 있습니다.'); } }
  function contextState() { if (context?.state !== 'running') interrupt(); }
  function input(lane, pressed, event) { if (state !== 'playing') return; const nowUs = timeUs(stamp(event)); engine.input(lane, pressed, nowUs); if (pressed) audio.hit?.(lane); updateJudgment(nowUs); }
  container.addEventListener('click', event => {
    const button = event.target.closest('button'); if (!button) return;
    if (button.dataset.step !== undefined) { move(Number(button.dataset.step)); return; }
    const action = button.dataset.tutorial;
    if (action === 'close') { cancel(); state = 'closed'; onClose(); }
    if (action === 'previous') move(step - 1);
    if (action === 'next') { if (step < 3) move(step + 1); else { cancel(); state = 'closed'; onClose(); } }
    if (action === 'start') { if (state === 'playing') { cancel(); ready('연습을 멈췄습니다. 준비되면 다시 시작하세요.'); } else if (state !== 'loading') start(); }
  }, { signal: abort.signal });
  window.addEventListener('keydown', event => {
    if (event.code === 'Escape') { event.preventDefault(); cancel(); state = 'closed'; onClose(); return; }
    const lane = keys.indexOf(event.code); if (!STEPS[step].lanes.includes(lane) || state !== 'playing') return;
    event.preventDefault(); if (event.repeat || held.has(event.code)) return; held.add(event.code); input(lane, true, event);
  }, { signal: abort.signal });
  window.addEventListener('keyup', event => { const lane = keys.indexOf(event.code); if (!STEPS[step].lanes.includes(lane)) return; held.delete(event.code); if (state === 'playing') { event.preventDefault(); input(lane, false, event); } }, { signal: abort.signal });
  container.addEventListener('pointerdown', event => { const pad = event.target.closest('[data-lane]'); if (!pad || state !== 'playing') return; event.preventDefault(); const lane = Number(pad.dataset.lane); pad.setPointerCapture?.(event.pointerId); if (!held.has(keys[lane])) { held.add(keys[lane]); input(lane, true, event); } }, { signal: abort.signal });
  function releasePointer(event) { const pad = event.target.closest('[data-lane]'); if (!pad) return; const lane = Number(pad.dataset.lane); if (held.has(keys[lane])) { held.delete(keys[lane]); input(lane, false, event); } }
  container.addEventListener('pointerup', releasePointer, { signal: abort.signal }); container.addEventListener('pointercancel', releasePointer, { signal: abort.signal });
  window.addEventListener('blur', interrupt, { signal: abort.signal }); document.addEventListener('visibilitychange', () => { if (document.hidden) interrupt(); }, { signal: abort.signal });
  ready();
  return { destroy() { if (destroyed) return; destroyed = true; state = 'destroyed'; cancel(); abort.abort(); container.classList.remove('personal-widget'); } };
}
