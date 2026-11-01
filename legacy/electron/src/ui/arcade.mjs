import { RhythmEngine } from '../core/judge.mjs';
import { mapOutputTimestamp } from '../core/audio.mjs';

const BEAT_US = 600_000;
const DURATION_US = 48 * BEAT_US;
const DIRECTIONS = ['←', '↓', '↑', '→'];
const MOVES = [[-1, 0], [0, 1], [0, -1], [1, 0]];
const META = {
  echo: { title: '에코 스튜디오', label: 'LISTEN & REPEAT', description: '4박을 듣고, 다음 4박에 같은 리듬을 재현하세요.', objective: '리듬 기억 · 청각 집중', help: '밝아지는 두 패드와 소리를 기억하세요. 응답 구간에는 정답 노트가 표시되지 않습니다.' },
  silence: { title: '빈 박자 챌린지', label: 'KEEP YOUR INNER PULSE', description: '클릭음이 사라져도 같은 간격으로 박자를 이어가세요.', objective: '내적 박 · 템포 유지', help: '처음 4박을 들은 뒤 왼쪽 키를 매 박 누릅니다. 무음 8박 동안에는 시각 박자 안내도 사라집니다.' },
  workshop: { title: '비트 공방', label: 'CRAFT ON THE BEAT', description: '정확한 탭과 홀드로 작은 부품을 완성하세요.', objective: '정확한 타격 · 홀드 유지', help: '들어오는 부품이 작업선에 닿을 때 해당 키를 누르세요. 길게 이어진 부품은 끝까지 유지합니다.' },
  guard: { title: '리듬 가드', label: 'READ · GUARD · HOLD', description: '좌우의 공격 예고를 읽고 박자에 맞춰 방어하세요.', objective: '좌우 대응 · 지속 방어', help: '방패 선에 공격이 닿을 때 해당 키를 누릅니다. 긴 공격은 끝까지 누르고 방어하세요.' },
  route: { title: '비트 루트', label: 'PLAN YOUR RHYTHM', description: '네 방향 명령을 기억하고 다음 마디에 실행하세요.', objective: '박 묶음 이해 · 순서 기억', help: '먼저 4개 명령과 목적지를 확인하세요. 다음 4박에 왼쪽부터 한 명령씩 해당 방향 키를 누릅니다.' },
};
const escapeHTML = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
const keyLabel = code => String(code).replace(/^Key/, '').replace(/^Digit/, '').replace('Arrow', '');
const clamp = (value, minimum, maximum) => Math.min(maximum, Math.max(minimum, value));

function makeSession(kind) {
  const notes = [], calls = [], routes = [];
  let serial = 0;
  const add = (lane, beat, hold = 0, pattern = kind) => notes.push({ id: `arcade-${kind}-${serial++}`, lane, timeUs: Math.round(beat * BEAT_US), ...(hold ? { endUs: Math.round((beat + hold) * BEAT_US) } : {}), pattern });
  if (kind === 'echo') {
    const rhythms = [[0, 1, 2, 3], [0, .5, 2, 3], [0, 1, 1.5, 3], [0, .5, 1.5, 2.5], [0, 1, 2, 2.5, 3.5], [0, .5, 1, 2, 3, 3.5]];
    rhythms.forEach((rhythm, round) => rhythm.forEach((offset, index) => {
      const lane = (index + round) % 2 ? 2 : 0;
      calls.push({ lane, timeUs: Math.round((round * 8 + offset) * BEAT_US) });
      add(lane, round * 8 + 4 + offset);
    }));
  } else if (kind === 'silence') {
    for (let beat = 4; beat < 48; beat++) add(0, beat, 0, 'pulse');
  } else if (kind === 'route') {
    const commands = [[3, 3, 1, 0], [0, 2, 3, 3], [1, 1, 3, 2], [2, 0, 0, 1], [3, 2, 2, 0], [0, 1, 1, 3]];
    commands.forEach((sequence, round) => {
      let x = 2, y = 2;
      for (const lane of sequence) { x += MOVES[lane][0]; y += MOVES[lane][1]; }
      routes.push({ sequence, goal: [x, y] });
      sequence.forEach((lane, index) => add(lane, round * 8 + 4 + index));
    });
  } else {
    for (let beat = 4, index = 0; beat < 47; beat += 2, index++) {
      const hold = index % (kind === 'guard' ? 4 : 3) === 2 ? 1.25 : 0;
      add(index % 2 ? 2 : 0, beat, hold, hold ? 'hold' : 'alternating');
      if (kind === 'workshop' && index % 5 === 4) add(index % 2 ? 0 : 2, beat + 1, 0, 'alternating');
    }
  }
  notes.sort((a, b) => a.timeUs - b.timeUs || a.lane - b.lane);
  return { chart: { id: `arcade-${kind}`, songId: `arcade-${kind}`, difficulty: 'arcade', version: '1', bpm: 100, durationUs: DURATION_US, notes }, calls, routes };
}

function installStyles() {
  if (document.getElementById('arcade-styles')) return;
  const style = document.createElement('style');
  style.id = 'arcade-styles';
  style.textContent = `
.arcade-play{max-width:1050px;margin:0 auto}.arcade-play .arcade-heading{display:flex;align-items:center;justify-content:space-between;gap:25px;margin-bottom:23px}.arcade-play h1{font-size:30px;font-weight:600;letter-spacing:-1px;margin:8px 0}.arcade-play .arcade-intro{font-size:12px;line-height:1.9;color:var(--sub);margin:0}.arcade-play .arcade-stage{padding:26px 32px;min-height:450px;position:relative;overflow:hidden}.arcade-play .arcade-stage-top{display:flex;justify-content:space-between;align-items:center;color:var(--muted);font-size:11px}.arcade-play .arcade-state{color:var(--accent);font-size:11px;letter-spacing:1px}.arcade-play .arcade-center{padding:14px 0;text-align:center}.arcade-play .arcade-center h2{margin:12px 0 9px;font-size:27px;font-weight:450;letter-spacing:-.7px}.arcade-play .arcade-instruction{min-height:21px;margin:0;color:var(--sub);font-size:12px;line-height:1.8}.arcade-play .arcade-feedback{height:30px;text-align:center;font-size:13px;letter-spacing:1.5px;color:var(--accent);margin:7px 0 0}.arcade-play .arcade-feedback small{font-size:10px;margin-left:9px;letter-spacing:0;color:var(--sub)}.arcade-play .arcade-pads{display:flex;gap:14px;max-width:450px;margin:16px auto 10px}.arcade-play .arcade-pad{flex:1;height:94px;background:#252d3a;border:1px solid #465163;border-radius:10px;color:#d0c8e8;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:9px;touch-action:none;user-select:none}.arcade-play .arcade-pad strong{font-size:23px;font-weight:450}.arcade-play .arcade-pad span{font-size:10px;color:var(--sub)}.arcade-play .arcade-pad.on{background:#343f41;border-color:#9cac87;color:#d4e6bd}.arcade-play .arcade-pad[data-demo="true"]{background:#343f4b;border-color:#aaa4cc}.arcade-play .arcade-pads.four .arcade-pad{height:66px}.arcade-play .arcade-pads.four .arcade-pad strong{font-size:20px}.arcade-play .arcade-dots{display:flex;justify-content:center;gap:13px;height:10px;margin:21px 0}.arcade-play .arcade-dots i{width:7px;height:7px;background:#394451;border-radius:50%}.arcade-play .arcade-dots i.on{background:#bdd098}.arcade-play .arcade-dots.is-silent i{visibility:hidden}.arcade-play .arcade-track{height:5px;border-radius:5px;background:#2a3440;margin:17px 0 0;overflow:hidden}.arcade-play .arcade-track i{display:block;height:100%;background:#809a68;width:0}.arcade-play .arcade-bottom{display:flex;justify-content:space-between;gap:20px;align-items:center;margin-top:17px;font-size:11px;color:var(--muted)}.arcade-play .arcade-bottom p{line-height:1.8;margin:0;max-width:650px}.arcade-play .arcade-keys{display:flex;gap:7px;align-items:center;white-space:nowrap}.arcade-play .arcade-start{min-width:150px;margin-top:24px}.arcade-play .arcade-canvas{width:100%;height:145px;display:block;margin:10px auto 2px}.arcade-play .arcade-route{display:grid;grid-template-columns:repeat(5,27px);gap:5px;justify-content:center;margin:12px auto}.arcade-play .arcade-tile{width:27px;height:27px;border-radius:5px;background:#242e3a;border:1px solid #364251;position:relative}.arcade-play .arcade-tile.goal{border-color:#aeaa7d;background:#37392e}.arcade-play .arcade-tile.goal:after{content:'◇';font-size:19px;color:#d6cea0;position:absolute;inset:0;display:grid;place-items:center}.arcade-play .arcade-tile.player:before{content:'';position:absolute;width:12px;height:12px;left:6px;top:6px;border-radius:4px;background:#bedb96;z-index:1}.arcade-play .arcade-commands{display:flex;justify-content:center;gap:8px;height:36px;margin:8px auto}.arcade-play .arcade-command{width:35px;height:35px;border:1px solid #455063;background:#293140;border-radius:6px;display:grid;place-items:center;font-size:19px;color:#c8c0e5}.arcade-play .arcade-command.done{background:#303b2a;border-color:#768e60;color:#c2dc9b}.arcade-play .arcade-command.miss{background:#3c3032;border-color:#976766;color:#d2a09d}.arcade-play .arcade-command.hidden-command{color:transparent;background:#222b36;border-color:#36404d}.arcade-play .arcade-results{display:grid;grid-template-columns:repeat(3,1fr);max-width:540px;gap:15px;margin:30px auto}.arcade-play .arcade-result{padding:20px 12px;background:#202936;border:1px solid #34404d;border-radius:9px}.arcade-play .arcade-result strong{display:block;font-size:27px;font-weight:450;letter-spacing:-.8px;margin-bottom:10px;color:#d2e4bf}.arcade-play .arcade-result span{font-size:10px;color:var(--sub)}.arcade-play .arcade-actions{display:flex;justify-content:center;gap:10px;margin-top:26px}.arcade-play .arcade-quiet{display:flex;align-items:center;justify-content:center;height:122px;margin:16px auto;border:1px dashed #3c4654;border-radius:10px;max-width:450px;color:#8e9aab;font-size:13px;letter-spacing:2px}.arcade-play .arcade-quiet span{width:35px;height:35px;display:grid;place-items:center;border:1px solid #526047;border-radius:50%;color:#b4c99c}.arcade-play .arcade-note{font-size:10px;line-height:1.9;color:var(--muted);margin-top:17px;text-align:center}@media(max-width:1100px){.arcade-play .arcade-stage{padding:24px}.arcade-play .arcade-bottom{align-items:flex-start}.arcade-play .arcade-bottom p{max-width:460px}}
`;
  document.head.append(style);
}

/** Five deterministic offline exercises sharing the standard audio clock and judgment engine. */
export function mountArcade(container, { kind = 'echo', audio, settings, onExit = () => {}, onComplete = () => {}, toast = () => {} }) {
  if (!META[kind]) throw new RangeError('Unknown arcade game');
  installStyles();
  const meta = META[kind], session = makeSession(kind), chart = session.chart;
  const keys = Array.isArray(settings.keys) ? [...settings.keys] : ['KeyD', 'KeyF', 'KeyJ', 'KeyK'];
  const activeLanes = kind === 'route' ? [0, 1, 2, 3] : kind === 'silence' ? [0] : [0, 2];
  const abort = new AbortController();
  const sources = new Set(), held = new Set();
  let master = null, context = null, observedContext = null, engine = null, frame = 0, generation = 0, startContextTime = 0;
  let state = 'ready', destroyed = false, feedback = null, round = -1, player = [2, 2], completedRoutes = 0;
  const handledHeads = new Set(), successfulRoutes = new Set();

  container.innerHTML = `<section class="arcade-play"><div class="arcade-heading"><div><span class="eyebrow">RHYTHM ARCADE · ${meta.label}</span><h1>${meta.title}</h1><p class="arcade-intro">${meta.description}</p></div><button class="btn ghost" data-action="exit">← 아케이드로</button></div><div class="panel arcade-stage"><div class="arcade-stage-top"><span class="pill lavender">${meta.objective}</span><span class="arcade-state" data-role="state">100 BPM · 약 30초</span></div><div class="arcade-center" data-role="center"></div><div class="arcade-track" data-role="progress-track"><i data-role="progress"></i></div></div><div class="arcade-bottom"><p>${meta.help}</p><div class="arcade-keys">${activeLanes.map(lane => `<span class="key">${escapeHTML(keyLabel(keys[lane]))}</span>`).join('')}<span>${kind === 'route' ? '← ↓ ↑ →' : kind === 'silence' ? '매 박 입력' : '왼쪽 · 오른쪽'}</span></div></div><p class="arcade-note">개인 연습 · 정규 기록과 별도 집계 · 입력 보정 ${(settings.inputOffsetUs / 1000).toFixed(1)}ms · ESC로 나가기</p></section>`;
  const center = container.querySelector('[data-role="center"]');
  const stateLabel = container.querySelector('[data-role="state"]');
  const progress = container.querySelector('[data-role="progress"]');
  let title, instruction, feedbackElement, dots, pads = [], canvas, grid, commands;

  function cleanSound() {
    for (const source of sources) { try { source.stop(); source.disconnect(); } catch { /* already stopped */ } }
    sources.clear();
    if (master) { try { master.disconnect(); } catch {} master = null; }
  }
  function stopRun() {
    generation++;
    cancelAnimationFrame(frame);
    cleanSound();
    held.clear();
  }
  function ready(message = null) {
    state = 'ready';
    stateLabel.textContent = '100 BPM · 약 30초';
    center.innerHTML = `<div style="padding:50px 0 38px"><span class="eyebrow">${message ? 'SESSION STOPPED' : 'READY WHEN YOU ARE'}</span><h2>${message ? '잠시 멈췄어요' : '작은 리듬에서 시작하세요'}</h2><p class="arcade-instruction">${message || meta.help}</p><div class="arcade-keys" style="justify-content:center;margin-top:25px">${activeLanes.map(lane => `<span class="key">${escapeHTML(keyLabel(keys[lane]))}</span>`).join('')}<span>지정된 키로 직접 연주</span></div><button class="btn primary arcade-start" data-action="start">${message ? '처음부터 재시도' : '준비 · 시작하기'}</button></div>`;
    progress.style.width = '0%';
  }
  function buildStage() {
    center.innerHTML = `<h2 data-role="title">준비 박자를 들어보세요</h2><p class="arcade-instruction" data-role="instruction">조금 뒤 시작합니다.</p>${kind === 'route' ? `<div class="arcade-route" data-role="grid">${Array.from({ length: 25 }, () => '<i class="arcade-tile"></i>').join('')}</div><div class="arcade-commands" data-role="commands">${Array.from({ length: 4 }, () => '<i class="arcade-command"></i>').join('')}</div>` : kind === 'workshop' || kind === 'guard' ? '<canvas class="arcade-canvas" data-role="canvas" aria-label="다가오는 부품과 공격의 시간 안내"></canvas>' : '<div class="arcade-quiet" data-role="quiet">소리와 간격에 집중하세요</div>'}<div class="arcade-dots" data-role="dots">${Array.from({ length: 4 }, () => '<i></i>').join('')}</div><div class="arcade-pads ${kind === 'route' ? 'four' : ''}">${activeLanes.map(lane => `<button class="arcade-pad" data-lane="${lane}" aria-label="${escapeHTML(keyLabel(keys[lane]))} ${kind === 'route' ? DIRECTIONS[lane] : lane === 0 ? '왼쪽' : '오른쪽'}"><strong>${kind === 'route' ? DIRECTIONS[lane] : escapeHTML(keyLabel(keys[lane]))}</strong><span>${kind === 'route' ? escapeHTML(keyLabel(keys[lane])) : kind === 'silence' ? '같은 간격으로' : lane === 0 ? 'LEFT' : 'RIGHT'}</span></button>`).join('')}</div><div class="arcade-feedback" data-role="feedback" aria-live="off"></div>`;
    title = center.querySelector('[data-role="title"]'); instruction = center.querySelector('[data-role="instruction"]');
    feedbackElement = center.querySelector('[data-role="feedback"]'); dots = center.querySelector('[data-role="dots"]');
    pads = [...center.querySelectorAll('[data-lane]')]; canvas = center.querySelector('canvas');
    grid = center.querySelector('[data-role="grid"]'); commands = center.querySelector('[data-role="commands"]');
  }
  function tone(lane, at, click = false) {
    const oscillator = context.createOscillator(), gain = context.createGain();
    oscillator.type = 'sine'; oscillator.frequency.value = click ? (lane ? 880 : 1050) : [440, 550, 660, 880][lane];
    const duration = click ? .035 : .13;
    gain.gain.setValueAtTime(.00001, at);
    gain.gain.exponentialRampToValueAtTime(click ? .11 : .2, at + .004);
    gain.gain.exponentialRampToValueAtTime(.00001, at + duration);
    oscillator.connect(gain).connect(master); oscillator.start(at); oscillator.stop(at + duration + .01);
    sources.add(oscillator);
    oscillator.onended = () => { sources.delete(oscillator); try { oscillator.disconnect(); gain.disconnect(); } catch {} };
  }
  function isSilent(beat) { return kind === 'silence' && (beat >= 16 && beat < 24 || beat >= 32 && beat < 40); }
  function schedule() {
    for (let beat = -2; beat < 48; beat++) if (!isSilent(beat)) tone(beat % 4 ? 1 : 0, startContextTime + beat * .6, true);
    if (kind === 'echo') for (const call of session.calls) tone(call.lane, startContextTime + call.timeUs / 1_000_000);
    else if (kind === 'workshop' || kind === 'guard') for (const note of chart.notes) tone(note.lane, startContextTime + note.timeUs / 1_000_000);
  }
  function clockUs(performanceTimeMs = performance.now()) {
    const timestamp = context.getOutputTimestamp?.();
    const outputTime = timestamp && timestamp.contextTime > 0 && timestamp.performanceTime > 0 && Number.isFinite(timestamp.contextTime) && Number.isFinite(timestamp.performanceTime)
      ? mapOutputTimestamp(timestamp, performanceTimeMs)
      : context.currentTime - (context.baseLatency || 0) - (context.outputLatency || 0) + (performanceTimeMs - performance.now()) / 1000;
    return Math.round((outputTime - startContextTime) * 1_000_000);
  }
  function eventTime(event) {
    const stamped = event.timeStamp > 1e12 ? event.timeStamp - performance.timeOrigin : event.timeStamp;
    return Number.isFinite(stamped) && Math.abs(stamped - performance.now()) < 60_000 ? stamped : performance.now();
  }
  function reflectJudgments(timeUs) {
    for (const judgment of engine.judgments) {
      const id = `${judgment.noteId}:${judgment.kind}`;
      if (handledHeads.has(id)) continue;
      handledHeads.add(id);
      feedback = { grade: judgment.grade, deltaUs: judgment.deltaUs, timeUs };
      if (kind === 'route' && judgment.kind === 'head') {
        const roundIndex = Math.floor(judgment.targetUs / (8 * BEAT_US));
        if (judgment.grade !== 'MISS') {
          const [dx, dy] = MOVES[judgment.lane];
          if (round === roundIndex) player = [clamp(player[0] + dx, 0, 4), clamp(player[1] + dy, 0, 4)];
        }
        const thisRound = engine.judgments.filter(event => event.kind === 'head' && Math.floor(event.targetUs / (8 * BEAT_US)) === roundIndex);
        if (thisRound.length === 4 && thisRound.every(event => event.grade !== 'MISS') && !successfulRoutes.has(roundIndex)) { successfulRoutes.add(roundIndex); completedRoutes++; }
      }
    }
  }
  function input(lane, pressed, performanceTimeMs) {
    if (state !== 'playing' || !engine) return;
    const nowUs = clockUs(performanceTimeMs);
    const correctedUs = nowUs - (settings.inputOffsetUs || 0);
    if (pressed && (kind === 'echo' || kind === 'route')) {
      const localBeat = ((correctedUs / BEAT_US) % 8 + 8) % 8;
      // Listen/plan inputs never become answer inputs or accidental ghost penalties.
      if (correctedUs < 0 || localBeat < 3.75) return;
    }
    if (pressed && kind === 'silence' && correctedUs < 4 * BEAT_US - 150_000) return;
    engine.input(lane, pressed, nowUs);
    if (pressed && kind !== 'silence') audio.hit?.(lane);
    reflectJudgments(nowUs);
  }
  function drawIncoming(visualUs) {
    const bounds = canvas.getBoundingClientRect(), ratio = window.devicePixelRatio || 1;
    const width = Math.max(100, bounds.width), height = 145;
    if (canvas.width !== Math.round(width * ratio) || canvas.height !== Math.round(height * ratio)) { canvas.width = Math.round(width * ratio); canvas.height = Math.round(height * ratio); }
    const ctx = canvas.getContext('2d');
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0); ctx.clearRect(0, 0, width, height);
    const horizon = Math.max(900_000, settings.approachMs * 1000), line = width * .24, right = width - 18;
    for (const [index, lane] of [0, 2].entries()) {
      const y = index ? 104 : 40;
      ctx.strokeStyle = '#34404c'; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(16, y); ctx.lineTo(right, y); ctx.stroke();
      ctx.fillStyle = '#8794a4'; ctx.font = '10px Pretendard, sans-serif'; ctx.fillText(lane === 0 ? 'LEFT' : 'RIGHT', 15, y - 17);
    }
    ctx.strokeStyle = '#a5bb87'; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(line, 15); ctx.lineTo(line, 128); ctx.stroke();
    ctx.fillStyle = '#94a685'; ctx.font = '9px Pretendard, sans-serif'; ctx.textAlign = 'center'; ctx.fillText(kind === 'guard' ? '방어선' : '작업선', line, 142); ctx.textAlign = 'left';
    for (const note of engine.notes) {
      if (note.state === 'miss' || note.state === 'hit' && note.endUs === undefined || note.holdState === 'complete' || note.holdState === 'broken') continue;
      const relative = note.timeUs - visualUs;
      const endRelative = (note.endUs ?? note.timeUs) - visualUs;
      if (relative > horizon || endRelative < -150_000) continue;
      const x = note.holdState === 'holding' ? line : line + relative / horizon * (right - line), y = note.lane === 0 ? 40 : 104;
      const endX = line + endRelative / horizon * (right - line);
      if (note.endUs) { ctx.fillStyle = '#52624a'; ctx.fillRect(Math.max(line, x), y - 7, Math.max(0, Math.min(right, endX) - Math.max(line, x)), 14); }
      ctx.fillStyle = note.lane === 0 ? '#b4c999' : '#b9b0d5';
      if (kind === 'guard') { ctx.beginPath(); ctx.moveTo(x, y - 12); ctx.lineTo(x + 13, y); ctx.lineTo(x, y + 12); ctx.lineTo(x - 13, y); ctx.closePath(); ctx.fill(); }
      else ctx.fillRect(Math.max(line - 8, x - 8), y - 10, 16, 20);
    }
  }
  function renderRoute(visualUs) {
    const nextRound = clamp(Math.floor(Math.max(0, visualUs) / (8 * BEAT_US)), 0, 5);
    if (nextRound !== round) { round = nextRound; player = [2, 2]; }
    const route = session.routes[round], planning = visualUs < (round * 8 + 4) * BEAT_US;
    const events = engine.judgments.filter(event => event.kind === 'head' && Math.floor(event.targetUs / (8 * BEAT_US)) === round);
    [...grid.children].forEach((tile, index) => { tile.classList.toggle('goal', index % 5 === route.goal[0] && Math.floor(index / 5) === route.goal[1]); tile.classList.toggle('player', index % 5 === player[0] && Math.floor(index / 5) === player[1]); });
    [...commands.children].forEach((element, index) => { const judgment = events.find(event => event.noteId === chart.notes[round * 4 + index].id); element.textContent = DIRECTIONS[route.sequence[index]]; element.className = `arcade-command${planning ? '' : judgment ? judgment.grade === 'MISS' ? ' miss' : ' done' : ' hidden-command'}`; });
    title.textContent = visualUs < 0 ? '목적지와 명령을 확인하세요' : planning ? `경로 ${round + 1} · 먼저 계획하세요` : `경로 ${round + 1} · 네 박에 실행`;
    instruction.textContent = planning ? '화살표 네 개를 왼쪽부터 기억하세요. ◇가 목적지입니다.' : `한 박에 한 명령 · 완료한 경로 ${completedRoutes}/6`;
  }
  function render(nowUs) {
    const visualUs = nowUs + (settings.visualOffsetUs || 0), beat = Math.floor(visualUs / BEAT_US);
    const local = ((beat % 8) + 8) % 8, silent = isSilent(beat);
    stateLabel.textContent = `${Math.min(48, Math.max(0, beat + 1))} / 48박 · ${engine.maxCombo} COMBO`;
    if (!silent) progress.style.width = `${clamp(nowUs / DURATION_US, 0, 1) * 100}%`;
    dots.classList.toggle('is-silent', silent);
    [...dots.children].forEach((dot, index) => dot.classList.toggle('on', visualUs >= 0 && beat % 4 === index));
    for (const pad of pads) {
      const lane = Number(pad.dataset.lane);
      pad.classList.toggle('on', held.has(keys[lane]));
      pad.dataset.demo = String(kind === 'echo' && session.calls.some(call => call.lane === lane && visualUs >= call.timeUs && visualUs < call.timeUs + 130_000));
    }
    if (kind === 'echo') {
      const currentRound = clamp(Math.floor(Math.max(0, visualUs) / (8 * BEAT_US)) + 1, 1, 6);
      title.textContent = visualUs < 0 ? '곧 첫 리듬을 들려드려요' : local < 4 ? `${currentRound}/6 · 먼저 들어보세요` : `${currentRound}/6 · 같은 리듬을 연주하세요`;
      instruction.textContent = local < 4 ? '왼쪽과 오른쪽 소리의 순서를 기억하세요.' : '들었던 순서와 간격으로 두 키를 눌러주세요.';
      const quiet = center.querySelector('[data-role="quiet"]');
      quiet.textContent = visualUs < 0 ? '준비 박자' : local < 4 ? 'LISTEN · 4 BEATS' : 'YOUR TURN · 4 BEATS';
    } else if (kind === 'silence') {
      title.textContent = visualUs < 4 * BEAT_US ? '처음 네 박은 들어보세요' : silent ? '소리 없이 · 같은 박을 이어가세요' : '클릭음에 맞춰 박자를 유지하세요';
      instruction.textContent = silent ? '무음 8박 · 박자 안내 없이 일정한 간격을 유지합니다.' : '왼쪽 키 하나로 매 박 입력 · 조금 뒤 클릭음이 사라집니다.';
      center.querySelector('[data-role="quiet"]').textContent = silent ? '내 안의 박자에 집중' : visualUs < 4 * BEAT_US ? 'LISTEN · 준비 4박' : 'CLICK · 매 박 입력';
      if (silent) stateLabel.textContent = '무음 구간 · 박자를 유지하세요';
    } else if (kind === 'route') renderRoute(visualUs);
    else {
      title.textContent = visualUs < 4 * BEAT_US ? '준비 박자를 들어보세요' : kind === 'guard' ? '예고를 읽고 · 정확하게 방어' : '박자에 맞춰 · 부품을 완성';
      instruction.textContent = kind === 'guard' ? '◇가 방어선에 닿을 때 입력 · 긴 공격은 키를 유지하세요.' : '부품이 작업선에 닿을 때 입력 · 연결된 부품은 끝까지 유지하세요.';
      drawIncoming(visualUs);
    }
    if (feedback && !silent && nowUs - feedback.timeUs < 650_000) {
      feedbackElement.innerHTML = `${feedback.grade}${settings.showTiming && feedback.deltaUs !== null ? `<small>${feedback.deltaUs > 0 ? '+' : ''}${(feedback.deltaUs / 1000).toFixed(1)}ms</small>` : ''}`;
      feedbackElement.style.color = feedback.grade === 'MISS' ? '#d7a49e' : '#c3d9a3';
    } else feedbackElement.textContent = '';
  }
  function finish() {
    if (state !== 'playing') return;
    const summary = engine.finish();
    state = 'finished'; stopRun();
    const sustain = summary.judgments.filter(event => event.kind === 'hold');
    const successes = summary.judgments.filter(event => event.kind === 'head' && event.grade !== 'MISS').length;
    const quality = summary.accuracy >= 95 ? '정밀한 완성' : summary.accuracy >= 80 ? '안정적인 흐름' : '연습 중인 리듬';
    const lastMetric = kind === 'route' ? [`${completedRoutes}/6`, '목적지까지 완료한 경로'] : kind === 'workshop' ? [`${sustain.filter(event => event.grade !== 'MISS').length}/${sustain.length}`, '완성한 홀드 부품'] : kind === 'guard' ? [`${successes}/${chart.notes.length}`, '방어한 공격'] : [`${summary.maxCombo}`, '최대 콤보'];
    stateLabel.textContent = 'SESSION COMPLETE'; progress.style.width = '100%';
    center.innerHTML = `<div style="padding:25px 0"><span class="eyebrow">${meta.title} · 연습 완료</span><h2>${quality}</h2><p class="arcade-instruction">${kind === 'silence' ? '소리가 없는 동안의 실제 입력도 같은 판정 기준으로 기록했습니다.' : '직접 연주한 입력으로만 계산한 결과입니다.'}</p><div class="arcade-results"><div class="arcade-result"><strong>${summary.accuracy.toFixed(2)}<small style="font-size:12px">%</small></strong><span>정확도</span></div><div class="arcade-result"><strong>${lastMetric[0]}</strong><span>${lastMetric[1]}</span></div><div class="arcade-result"><strong>${summary.counts.MISS}</strong><span>MISS · 홀드 포함</span></div></div><div class="arcade-actions"><button class="btn primary" data-action="start">같은 패턴 다시 연습</button><button class="btn" data-action="exit">아케이드로 돌아가기</button></div><p class="arcade-note">각 모드는 독립된 연습 기록이며, 정규 곡의 최고 기록에 포함되지 않습니다.</p></div>`;
    try { onComplete({ kind, summary, chart, score: summary.score }); } catch (error) { toast(error.message || '연습 결과를 저장하지 못했습니다.'); }
  }
  function tick() {
    if (destroyed || state !== 'playing') return;
    const nowUs = clockUs();
    engine.advance(nowUs); reflectJudgments(nowUs); render(nowUs);
    const endUs = DURATION_US + Math.max(0, settings.inputOffsetUs || 0) + 150_000;
    if (nowUs >= endUs) finish(); else frame = requestAnimationFrame(tick);
  }
  async function start() {
    if (destroyed || state === 'loading' || state === 'playing') return;
    stopRun(); const token = generation;
    state = 'loading';
    const button = center.querySelector('[data-action="start"]'); if (button) { button.disabled = true; button.textContent = '오디오 준비 중…'; }
    try {
      audio.stop?.(); await audio.init();
      if (destroyed || token !== generation) return;
      context = audio.context;
      if (!context) throw new Error('오디오 장치를 시작할 수 없습니다.');
      if (observedContext !== context) {
        observedContext?.removeEventListener('statechange', onContextState);
        observedContext = context;
        context.addEventListener('statechange', onContextState, { signal: abort.signal });
      }
      master = context.createGain(); master.gain.value = clamp(settings.volume ?? .7, 0, 1) * .65; master.connect(context.destination);
      engine = new RhythmEngine(chart, { inputOffsetUs: settings.inputOffsetUs || 0, rate: 1, windowScale: 1 });
      handledHeads.clear(); successfulRoutes.clear(); held.clear(); completedRoutes = 0; round = -1; player = [2, 2]; feedback = null;
      startContextTime = context.currentTime + 1.5;
      schedule(); buildStage(); state = 'playing'; frame = requestAnimationFrame(tick);
    } catch (error) {
      if (!destroyed && token === generation) { stopRun(); ready(error.message || '오디오를 준비하지 못했습니다.'); toast(error.message || '오디오를 준비하지 못했습니다.'); }
    }
  }
  function interrupt() {
    if (state === 'playing' || state === 'loading') { stopRun(); ready('화면을 벗어나 연습을 멈췄습니다. 준비되면 처음부터 시작하세요.'); }
  }
  function onContextState() { if (context.state !== 'running') interrupt(); }
  container.addEventListener('click', event => {
    const action = event.target.closest('[data-action]')?.dataset.action;
    if (action === 'start') start();
    if (action === 'exit') { state = 'exiting'; stopRun(); onExit(); }
  }, { signal: abort.signal });
  window.addEventListener('keydown', event => {
    if (event.code === 'Escape') { event.preventDefault(); state = 'exiting'; stopRun(); onExit(); return; }
    const lane = keys.indexOf(event.code);
    if (!activeLanes.includes(lane) || state !== 'playing') return;
    event.preventDefault(); if (event.repeat || held.has(event.code)) return;
    held.add(event.code); input(lane, true, eventTime(event));
  }, { signal: abort.signal });
  window.addEventListener('keyup', event => {
    const lane = keys.indexOf(event.code);
    if (!activeLanes.includes(lane)) return;
    held.delete(event.code); if (state === 'playing') { event.preventDefault(); input(lane, false, eventTime(event)); }
  }, { signal: abort.signal });
  container.addEventListener('pointerdown', event => {
    const pad = event.target.closest('[data-lane]'); if (!pad || state !== 'playing') return;
    event.preventDefault(); const lane = Number(pad.dataset.lane); pad.setPointerCapture?.(event.pointerId);
    if (held.has(keys[lane])) return; held.add(keys[lane]); input(lane, true, eventTime(event));
  }, { signal: abort.signal });
  const pointerUp = event => {
    const pad = event.target.closest('[data-lane]'); if (!pad) return;
    const lane = Number(pad.dataset.lane); if (!held.has(keys[lane])) return;
    held.delete(keys[lane]); input(lane, false, eventTime(event));
  };
  container.addEventListener('pointerup', pointerUp, { signal: abort.signal });
  container.addEventListener('pointercancel', pointerUp, { signal: abort.signal });
  window.addEventListener('blur', interrupt, { signal: abort.signal });
  document.addEventListener('visibilitychange', () => { if (document.hidden) interrupt(); }, { signal: abort.signal });
  ready();
  return { destroy() { if (destroyed) return; destroyed = true; state = 'destroyed'; stopRun(); abort.abort(); } };
}
